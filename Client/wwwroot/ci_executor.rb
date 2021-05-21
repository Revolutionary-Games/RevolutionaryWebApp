#!/usr/bin/env ruby
# frozen_string_literal: true

# This is the CI executor to run on the build machine for ThriveDevCenter

require 'English'

require 'fileutils'
require 'faye/websocket'
require 'eventmachine'
require 'json'
require 'open3'
require 'yaml'

REMOTE = 'origin'
PULL_REQUEST_REF_SUFFIX = '/head'
NORMAL_REF_PREFIX = 'refs/heads/'
HOME_FOLDER = File.expand_path '~'
IMAGE_CACHE_FOLDER = File.join HOME_FOLDER, 'images/'
CI_IMAGE_FILE = File.join IMAGE_CACHE_FOLDER, ENV['CI_IMAGE_FILENAME']
LOCAL_BRANCH = ENV['CI_BRANCH']
OUTPUT_SPECIAL_COMMAND_MARKER = '#--@%-DevCenter-%@--'
CI_JOB_NAME = ENV['CI_JOB_NAME']

# True when this is directly from the repo, false if this is a fork
BUILD_IS_SAFE = ENV['CI_TRUSTED'].downcase == 'true'

CACHE_BASE_FOLDER = BUILD_IS_SAFE ? '/executor_cache/safe' : '/executor_cache/unsafe'
SHARED_CACHE_FOLDER = File.join CACHE_BASE_FOLDER, 'shared'
JOB_CACHE_BASE_FOLDER = File.join CACHE_BASE_FOLDER, 'named'

BATCH_SEND_OUTPUT_SIZE = 4096
DAEMONIZE = true

def fail_with_error(error)
  puts error
  exit 1
end

def check_run(*cmd_and_args)
  Open3.popen2e(*cmd_and_args) do |stdin, stdout_and_stderr, wait_thr|
    stdin.close

    out_thread = Thread.new do
      begin
        stdout_and_stderr.each do |line|
          queue_send({ Type: 'BuildOutput', Output: line })
        end
      rescue IOError => e
        puts "out thread read failed: #{e}"
      end
    end

    puts 'Waiting for command to end'
    exit_status = wait_thr.value
    puts "Command now has a result: #{exit_status}"
    begin
      out_thread.join
    rescue StandardError => e
      puts "Failed to join the output listen threads for popen: #{e}"
    end

    raise "Running command failed: #{cmd_and_args[0]}" if exit_status != 0
  end
end

# Run with stdin content for a command, also parses section starts, ends, and statuses
def run_with_input(input, *cmd_and_args)
  Open3.popen2e(*cmd_and_args) do |stdin, stdout_and_stderr, wait_thr|
    in_thread = Thread.new do
      begin
        input.each_line { |line|
          stdin.write line
        }
      rescue IOError => e
        puts "stdin thread write failed: #{e}"
      end

      begin
        stdin.close
      rescue StandardError
        puts 'Failed to close stdin for command'
      end
    end

    out_thread = Thread.new do
      begin
        stdout_and_stderr.each do |line|
          if line.start_with? OUTPUT_SPECIAL_COMMAND_MARKER
            # A special command

            parts = line.split ' '

            if parts[1] == 'SectionEnd'

              success = parts[2].strip == '0'

              @failed_sections = true unless success

              queue_send({ Type: 'SectionEnd', WasSuccessful: success })
              @last_section_closed = true
            elsif parts[1] == 'SectionStart'
              queue_send({ Type: 'SectionStart',
                           SectionName: parts[2..-1].each(&:strip).join(' ') })
              @last_section_closed = false
            else
              queue_send(
                { Type: 'BuildOutput',
                  Output: 'Unknown special command received from build process' }
              )
              @failed_sections = true
            end
          else
            # Normal output
            queue_send({ Type: 'BuildOutput', Output: line })
          end
        end
      rescue IOError => e
        puts "out thread read failed: #{e}"
      end
    end

    puts 'Waiting for input reading command to end'
    exit_status = wait_thr.value
    puts "Command now has a result: #{exit_status}"
    begin
      in_thread.kill
      in_thread.join
      out_thread.join
    rescue StandardError => e
      puts "Failed to kill and join the stream threads for popen: #{e}"
    end

    exit_status.exitstatus
  end
end

def detect_and_setup_local_ref(folder, remote_ref, commit)
  if pull_request_ref? remote_ref
    check_run 'git', 'fetch', REMOTE, "#{remote_ref}:#{LOCAL_BRANCH}", { chdir: folder }
  else
    check_run 'git', 'fetch', REMOTE, remote_ref, { chdir: folder }
  end

  check_run 'git', 'checkout', commit, '--force', { chdir: folder }
  check_run 'git', 'clean', '-f', '-d', { chdir: folder }
end

def pull_request_ref?(remote_ref)
  remote_ref.start_with? 'pull/'
end

def handle_cache_templates(str)
  str.gsub('{Branch}', LOCAL_BRANCH)
end

def send_json(socket, obj)
  as_str = JSON.dump(obj)
  # puts "Sending message: #{as_str}"

  # Size first
  socket.send([as_str.length].pack('<l').bytes)

  # Then the data
  socket.send(as_str)
end

def queue_send(obj)
  # TODO: comment out
  puts "queue send: #{obj}"

  @output_mutex.synchronize {
    handled = false

    if obj['Type'] == 'BuildOutput'
      existing = @websocket_output_queue.last
      if !existing.nil? && existing['Type'] == obj['Type'] &&
         existing['Output'].length + obj['Output'].length < BATCH_SEND_OUTPUT_SIZE
        existing['Output'] += obj['Output']
        handled = true
      end
    end

    return if handled

    @websocket_output_queue.append obj
  }
end

def parse_config_to_build_command(config_path, job_name)
  unless File.exist? config_path
    queue_send({ Type: 'BuildOutput',
                 Output: "Config file is missing at path: #{config_path}\n" })
    return nil
  end

  build_folder = File.dirname(config_path)

  config = YAML.safe_load File.read(config_path)

  job_config = config['jobs'][job_name]

  unless job_config
    queue_send({ Type: 'BuildOutput',
                 Output: "Config file is missing current job: #{job_name}\n" })
    return nil
  end

  # Startup section
  build_command = <<~SCRIPT
    echo 'Starting running build in container'
    cd '#{build_folder}' || { echo "Couldn't switch to build folder"; exit 1; }
    echo 'Starting build commands'
    echo '#{OUTPUT_SPECIAL_COMMAND_MARKER} SectionEnd 0'
  SCRIPT

  # Build commands
  job_config['steps'].each { |step|
    run_step = step['run']
    name = run_step['name'] || run_step['command'][0..70]

    build_command += "echo '#{OUTPUT_SPECIAL_COMMAND_MARKER} SectionStart #{name}'\n"

    build_command += "(\n"
    build_command += "set -e\n"

    run_step['command'].each_line { |line|
      build_command += "#{line.strip}\n"
    }

    build_command += "\n)\n"
    build_command += "echo \"#{OUTPUT_SPECIAL_COMMAND_MARKER} SectionEnd $?\"\n"
  }

  build_command
end

#
# End of functions, start of main code
#

puts 'CI executor script starting'

connect_url = ARGV[0].sub('https://', 'wss://').sub('http://', 'ws://')

fail_with_error 'Build status report URL is empty' if connect_url.nil? || connect_url == ''

puts "Going to parse cache options: #{ENV['CI_CACHE_OPTIONS']}"
CACHE_CONFIG = JSON.parse ENV['CI_CACHE_OPTIONS']

CURRENT_BUILD_ROOT_FOLDER = File.join(JOB_CACHE_BASE_FOLDER,
                                      handle_cache_templates(CACHE_CONFIG['write_to']))
CACHE_COPY_FROM_FOLDERS = CACHE_CONFIG['load_from'].map { |path|
  File.join JOB_CACHE_BASE_FOLDER, handle_cache_templates(path)
}

FileUtils.mkdir_p JOB_CACHE_BASE_FOLDER
FileUtils.mkdir_p SHARED_CACHE_FOLDER
FileUtils.mkdir_p IMAGE_CACHE_FOLDER

if DAEMONIZE
  puts 'Daemonizing rest of job running'

  raise 'Fork failed' if (pid = fork) == -1

  exit unless pid.nil?

  Process.setsid

  $stdin.reopen '/dev/null'
  $stdout.reopen 'build_script_output.txt', 'w'
  $stderr.reopen $stdout

  puts 'Daemonizing succeeded'
end

@websocket_output_queue = []
@socket_open = false
@output_mutex = Mutex.new
@socket_mutex = Mutex.new
@close_requested = false

def mount_configuration
  folder = CURRENT_BUILD_ROOT_FOLDER
  shared_folder = SHARED_CACHE_FOLDER
  ['--mount', "type=bind,source=#{folder},destination=#{folder},relabel=shared",
   '--mount', "type=bind,source=#{shared_folder},destination=#{shared_folder},relabel=shared"]
end

def on_failed_phase(message)
  puts 'Queueing fail message'
  queue_send({ Type: 'BuildOutput', Output: "#{message}\n" })
  queue_send({ Type: 'SectionEnd', WasSuccessful: false })
  queue_send({ Type: 'FinalStatus', WasSuccessful: false })

  puts 'Requesting close due to error'
  @close_requested = true
end

def cache_setup
  queue_send({ Type: 'SectionStart', SectionName: 'Environment setup' })
  queue_send({ Type: 'BuildOutput', Output: "Starting cache setup\n" })

  unless File.exist? CURRENT_BUILD_ROOT_FOLDER
    queue_send(
      {
        Type: 'BuildOutput',
        Output: "Cache folder doesn't exist yet (#{CURRENT_BUILD_ROOT_FOLDER})\n"
      }
    )

    CACHE_COPY_FROM_FOLDERS.each { |cache|
      next unless File.exist? cache

      check_run 'cp', '-a', cache, CURRENT_BUILD_ROOT_FOLDER
      queue_send({ Type: 'BuildOutput', Output: "Found existing cache: #{cache}\n" })
      break
    }

  end
rescue StandardError => e
  on_failed_phase "Error setting up caches: #{e}"
end

def repo_clone
  queue_send({ Type: 'BuildOutput', Output: "Checking out needed ref: #{ENV['CI_REF']} " \
                                    "and commit: #{ENV['CI_COMMIT_HASH']}\n" })

  unless File.exist? CURRENT_BUILD_ROOT_FOLDER
    check_run 'git', 'clone', ENV['CI_ORIGIN'], CURRENT_BUILD_ROOT_FOLDER
  end

  check_run 'git', 'remote', 'set-url', REMOTE, ENV['CI_ORIGIN'],
            { chdir: CURRENT_BUILD_ROOT_FOLDER }

  detect_and_setup_local_ref CURRENT_BUILD_ROOT_FOLDER, ENV['CI_REF'],
                             ENV['CI_COMMIT_HASH']

  # TODO: implement symlinked shared cache parts

  queue_send({ Type: 'BuildOutput', Output: "Repository checked out\n" })
rescue StandardError => e
  on_failed_phase "Error cloning / checking out: #{e}"
end

def image_setup
  queue_send({ Type: 'BuildOutput',
               Output: "Using build environment image: #{ENV['CI_IMAGE_NAME']}\n" })

  image_folder = File.dirname(CI_IMAGE_FILE)

  queue_send(
    { Type: 'BuildOutput',
      Output: "Storing images in #{image_folder}\n" }
  )

  unless File.exist? CI_IMAGE_FILE
    # Create the cache folder

    FileUtils.mkdir_p image_folder

    queue_send(
      { Type: 'BuildOutput',
        Output: "Build environment image doesn't exist locally, downloading...\n" }
    )

    check_run 'curl', '-LsS', ENV['CI_IMAGE_DL_URL'], '--output', CI_IMAGE_FILE
  end

  check_run 'podman', 'load', '-i', CI_IMAGE_FILE

  queue_send({ Type: 'BuildOutput', Output: "Build environment image loaded\n" })
  queue_send({ Type: 'SectionEnd', WasSuccessful: true })
rescue StandardError => e
  on_failed_phase "Error handling build image: #{e}"
end

def run_in_podman
  queue_send({ Type: 'SectionStart', SectionName: 'Build start' })
  queue_send({ Type: 'BuildOutput',
               Output: "Using build environment image: #{ENV['CI_IMAGE_NAME']}\n" })

  build_command = parse_config_to_build_command(
    File.join(CURRENT_BUILD_ROOT_FOLDER, 'CIConfiguration.yml'), CI_JOB_NAME
  )

  raise 'Failed to parse CI configuration to build list of build commands' unless build_command

  puts "Build command is: #{build_command}"

  @last_section_closed = false
  @failed_sections = false
  result = run_with_input build_command, 'podman', 'run', '--rm', '-i', '-e',
                          "CI_REF=#{ENV['CI_REF']}", *mount_configuration,
                          ENV['CI_IMAGE_NAME'], '/bin/bash'

  if result.zero?
    unless @last_section_closed
      queue_send({ Type: 'BuildOutput', Output: "Build commands completed\n" })
    end
  else
    queue_send({ Type: 'BuildOutput', Output: "Build commands failed\n" })
    @failed_sections = true
  end

  queue_send({ Type: 'SectionEnd', WasSuccessful: !@failed_sections })
rescue StandardError => e
  on_failed_phase "Error running build commands: #{e}"
end

def handle_end
  # TODO: artifacts

  # send final status
  queue_send({ Type: 'FinalStatus', WasSuccessful: !@failed_sections })

  puts 'Requesting close due to reaching end of the build'
  @close_requested = true
end

actual_work_thread = Thread.new {
  begin
    puts 'Starting cache setup'
    cache_setup
    return if @close_requested

    puts 'Starting repo clone'
    repo_clone
    return if @close_requested

    puts 'Starting image setup'
    image_setup
    return if @close_requested

    puts 'Starting run in podman'
    run_in_podman
    return if @close_requested

    puts 'Starting end'
    handle_end
  rescue StandardError => e
    on_failed_phase "Error running build: #{e}"
  end
}

# Start websocket connection for sending output and then run the build after setup with podman
EM.run {
  ws = Faye::WebSocket::Client.new(connect_url, [], { ping: 55 })

  ws.on :open do |_event|
    puts 'Connected with websocket'

    @socket_mutex.synchronize {
      @socket_open = true
    }
  end

  ws.on :message do |event|
    puts 'Received websocket message from the server'
    # TODO: implement parsing it
    puts event.data
  end

  ws.on :error do |error|
    puts "websocket error: #{error}"
  end

  ws.on :close do |event|
    puts "Websocket closed, code: #{event.code}, reason: #{event.reason}"
    puts "Client status: #{ws.status}, headers: #{ws.headers}"
    @socket_mutex.synchronize {
      ws = nil
      @socket_open = false
    }

    puts 'Signaling stop_event_loop (in one second) as our socket is closed'
    EventMachine.add_timer(1) {
      puts 'Signaling event machine stop (closed socket)'
      EventMachine.stop_event_loop
    }
  end

  EventMachine.add_periodic_timer(1) {
    begin
      # Send output once every second
      send_messages = nil

      @socket_mutex.synchronize {
        @output_mutex.synchronize {
          return if @websocket_output_queue.empty?

          send_messages = @websocket_output_queue.dup
          @websocket_output_queue.clear
        }

        send_messages.each { |message|
          begin
            send_json(ws, message)
          rescue StandardError => e
            puts "Error sending output (#{message}): #{e}"
          end
        }
      }
    rescue StandardError => e
      puts "Error in send timer: #{e}"
    end
  }

  # This is a defer block to keep eventmachine running
  EventMachine.defer(
    proc {
      begin
        loop do
          # Check stop
          sleep 0.25
          next unless @close_requested

          puts 'Shutdown requested...'
          not_yet = false
          @output_mutex.synchronize {
            unless @websocket_output_queue.empty?
              puts 'We still have messages to send'
              not_yet = true
            end
          }

          next if not_yet

          puts 'Proceeding to shutdown'

          puts 'Closing our socket to stop us...'
          @socket_mutex.synchronize {
            begin
              ws&.close
            rescue StandardError => e
              puts "Failed to close websocket: #{e}"
              EventMachine.stop_event_loop
              return
            end
          }

          # Wait while the socket closes
          puts 'Waiting until the socket is closed'

          loop do
            sleep 0.5
            @socket_mutex.synchronize {
              break if ws.nil?
            }
          end

          puts 'Socket closed, ending this defer block'

          return
        end
      rescue StandardError => e
        puts "Error in shutdown detection timer: #{e}"
      end
    }
  )

  EventMachine.add_shutdown_hook {
    puts 'eventmachine shutting down'

    @output_mutex.synchronize {
      unless @websocket_output_queue.empty?
        puts 'Some messages could not be sent before socket close'
      end
    }
  }
}

puts 'Joining actual work thread'
actual_work_thread.join
puts 'Exiting CI executor script'
