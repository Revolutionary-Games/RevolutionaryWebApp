#!/usr/bin/env ruby
# frozen_string_literal: true

# This is the CI executor to run on the build machine for ThriveDevCenter

require 'English'

require 'fileutils'
require 'faye/websocket'
require 'eventmachine'
require 'json'

REMOTE = 'origin'
PULL_REQUEST_REF_SUFFIX = '/head'
NORMAL_REF_PREFIX = 'refs/heads/'
IMAGE_CACHE_FOLDER = '~/images/'
DAEMONIZE = true

def fail_with_error(error)
  puts error
  exit 1
end

def check_run(*command)
  system(*command)
  fail_with_error "Running command failed: #{command}" if $CHILD_STATUS.exitstatus != 0
end

def detect_local_ref(remote_ref)
  local_heads_ref = "refs/remotes/#{REMOTE}/"

  if pull_request_ref? remote_ref
    if remote_ref.end_with? PULL_REQUEST_REF_SUFFIX
      local_branch = remote_ref[0..(remote_ref.length - 1 - PULL_REQUEST_REF_SUFFIX.length)]
    else
      fail_with_error "Unrecognized PR ref: #{remote_ref}"
    end
  elsif remote_ref.start_with? NORMAL_REF_PREFIX
    local_branch = remote_ref[NORMAL_REF_PREFIX.length..-1]
  else
    fail_with_error "Unrecognized normal ref: #{remote_ref}"
  end

  local_heads_ref += local_branch

  [local_heads_ref, local_branch]
end

def detect_and_setup_local_ref(remote_ref, commit)
  _local_heads_ref, local_branch = detect_local_ref remote_ref

  if pull_request_ref? remote_ref
    check_run 'git', 'fetch', REMOTE, "#{remote_ref}:#{local_branch}"
  else
    check_run 'git', 'fetch', REMOTE, remote_ref
  end

  check_run 'git', 'checkout', commit, '--force'
  check_run 'git', 'clean', '-f', '-d'
end

def pull_request_ref?(remote_ref)
  remote_ref.start_with? 'pull/'
end

def send_json(socket, obj)
  as_str = JSON.dump(obj)
  # Size first
  socket.send([as_str.length].pack('<l').bytes)

  # Then the data
  socket.send(as_str)
end

def queue_send(obj)
  @output_mutex.synchronize {
    # TODO: this should combine messages together if they are less
    # than 4000 characters in length
    @websocket_output_queue.append obj
  }
end

#
# End of functions, start of main code
#

puts 'CI executor script starting'

connect_url = ARGV[0].sub('https://', 'wss://').sub('http://', 'ws://')

fail_with_error 'Build status report URL is empty' if connect_url.nil? || connect_url == ''

# For now just check that this runs correctly without error (as we can more easily report
# an error here)
detect_local_ref ENV['CI_REF']

@cache_config = JSON.parse ENV['CI_CACHE_OPTIONS']

# True when this is directly from the repo, false if this is a fork
# TODO: implement detecting this
@build_is_safe = true

CACHE_BASE_FOLDER = @build_is_safe ? '/executor_cache/safe' : '/executor_cache/unsafe'

SHARED_CACHE_FOLDER = File.join CACHE_BASE_FOLDER, 'shared'
JOB_CACHE_BASE_FOLDER = File.join CACHE_BASE_FOLDER, 'named'

CURRENT_BUILD_ROOT_FOLDER = File.join JOB_CACHE_BASE_FOLDER, @cache_config['write_to']
CACHE_COPY_FROM_FOLDERS = @cache_config['load_from'].map { |path|
  File.join JOB_CACHE_BASE_FOLDER, path
}

CI_IMAGE_FILE = File.join IMAGE_CACHE_FOLDER, ENV['CI_IMAGE_FILENAME']

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

def mount_configuration
  folder = CURRENT_BUILD_ROOT_FOLDER
  shared_folder = SHARED_CACHE_FOLDER
  ['--mount', "type=bind,source=#{folder},destination=#{folder},relabel=shared",
   '--mount', "type=bind,source=#{shared_folder},destination=#{shared_folder},relabel=shared"]
end

def on_failed_phase(message)
  queue_send({ Type: 'BuildOutput', Output: message })
  queue_send({ Type: 'SectionEnd', WasSuccessful: false })
  queue_send({ Type: 'FinalStatus', WasSuccessful: false })
  EventMachine.stop_event_loop
end

def defer_cache_setup
  EventMachine.defer(
    proc {
      queue_send({ Type: 'SectionStart', SectionName: 'Environment setup' })
      queue_send({ Type: 'BuildOutput', Output: 'Starting cache setup' })

      unless File.exist? CURRENT_BUILD_ROOT_FOLDER
        queue_send({ Type: 'BuildOutput',
                     Output: "Cache folder doesn't exist yet (#{CURRENT_BUILD_ROOT_FOLDER})" })

        CACHE_COPY_FROM_FOLDERS.each { |cache|
          next unless File.exist? cache

          check_run 'cp', '-a', cache, CURRENT_BUILD_ROOT_FOLDER
          queue_send({ Type: 'BuildOutput', Output: "Found existing cache: #{cache}" })
          break
        }

      end
    }, proc { |_result|
      defer_repo_clone
    }, proc { |error|
      on_failed_phase "Error setting up caches: #{error}"
    }
  )
end

def defer_repo_clone
  EventMachine.defer(
    proc {
      queue_send({ Type: 'BuildOutput', Output: "Checking out needed ref: #{ENV['CI_REF']} " \
                                        "and commit: #{ENV['CI_COMMIT_HASH']}" })

      unless File.exist? CURRENT_BUILD_ROOT_FOLDER
        check_run 'git', 'clone', ENV['CI_ORIGIN'], CURRENT_BUILD_ROOT_FOLDER
      end

      check_run 'git', 'remote', 'set-url', REMOTE, ENV['CI_ORIGIN']

      detect_and_setup_local_ref ENV['CI_REF'], ENV['CI_COMMIT_HASH']

      # TODO: implement symlinked shared cache parts

      queue_send({ Type: 'BuildOutput', Output: 'Repository checked out' })
    }, proc { |_result|
      defer_image_setup
    }, proc { |error|
      on_failed_phase "Error cloning / checking out: #{error}"
    }
  )
end

def defer_image_setup
  EventMachine.defer(
    proc {
      queue_send({ Type: 'BuildOutput',
                   Output: "Using build environment image: #{ENV['CI_IMAGE_NAME']}" })

      unless File.exist? CI_IMAGE_FILE
        queue_send({ Type: 'BuildOutput',
                     Output: "Build environment image doesn't exist locally, downloading..." })

        check_run 'curl', '-L', ENV['CI_IMAGE_DL_URL'], '--output', CI_IMAGE_FILE
      end

      check_run 'podman', 'load', '-i', CI_IMAGE_FILE

      queue_send({ Type: 'BuildOutput', Output: 'Build environment image loaded' })
      queue_send({ Type: 'SectionEnd', WasSuccessful: true })
    }, proc { |_result|
      defer_run_in_podman
    }, proc { |error|
      on_failed_phase "Error handling build image: #{error}"
    }
  )
end

def defer_run_in_podman
  EventMachine.defer(
    proc {
      queue_send({ Type: 'BuildOutput',
                   Output: "Using build environment image: #{ENV['CI_IMAGE_NAME']}" })

      # TODO: build the build commands into a single string to be ran in bash
      build_command = "set -e && cd #{CURRENT_BUILD_ROOT_FOLDER} && " \
                      "echo build command would go here\necho second line && true"

      check_run 'podman', 'run', '--rm', '-e', "CI_REF=#{ENV['CI_REF']}", *mount_configuration,
                ENV['CI_IMAGE_NAME'], build_command

      queue_send({ Type: 'BuildOutput', Output: 'Build commands completed' })
      queue_send({ Type: 'SectionEnd', WasSuccessful: true })
    }, proc { |_result|
      defer_end
    }, proc { |error|
      on_failed_phase "Error running build commands: #{error}"
    }
  )
end

def defer_end
  # TODO: artifacts

  # send final status
  queue_send({ Type: 'FinalStatus', WasSuccessful: true })

  # TODO: come up with a better design here (maybe the stop callback could send
  # pending messages)
  EventMachine.add_timer(5) {
    # For now just wait a bit before stopping eventmachine to send the last logs
    EventMachine.stop_event_loop
  }
end

# Start websocket connection for sending output and then run the build after setup with podman
EM.run {
  ws = Faye::WebSocket::Client.new(connect_url, [], { ping: 55 })

  ws.on :open do |_event|
    puts 'Connected with websocket, sending initial section...'

    @socket_mutex.synchronize {
      send_json(ws, { Type: 'SectionStart', SectionName: 'Environment setup' })

      puts 'Initial section opening sent'
      @socket_open = true
    }
  end

  ws.on :message do |event|
    puts 'Received websocket message from the server'
    # TODO: implement parsing it
    puts event.data
  end

  ws.on :close do |event|
    puts "Remote side closed websocket, code: #{event.code}, reason: #{event.reason}"
    puts "Client status: #{ws.status}, headers: #{ws.headers}"
    @socket_mutex.synchronize {
      ws = nil
      @socket_open = false
    }
  end

  defer_cache_setup

  EventMachine.add_periodic_timer(1) {
    # Send output once every second
    send_messages = nil
    @output_mutex.synchronize {
      return if @websocket_output_queue.empty?

      send_messages = @websocket_output_queue.dup
    }

    return if send_messages.nil? || send_messages.empty?

    @socket_mutex.synchronize {
      send_messages.each { |message|
        begin
        rescue StandardError => e
          puts "Error sending output (#{message}): #{e}"
        end
      }
    }
  }

  EventMachine.add_shutdown_hook {
    puts 'Performing eventmachine shutdown'

    @socket_mutex.synchronize {
      begin
        ws&.close
      rescue StandardError => e
        puts "Failed to close websocket: #{e}"
      end
    }
  }
}

puts 'Exiting CI executor script'
