#!/usr/bin/env ruby
# frozen_string_literal: true

# This is the CI executor to run on the build machine for ThriveDevCenter

require 'English'

require 'faye/websocket'
require 'eventmachine'
require 'json'

REMOTE = 'origin'
PULL_REQUEST_REF_SUFFIX = '/head'
NORMAL_REF_PREFIX = 'refs/heads/'
DAEMONIZE = true

def fail_with_error(error)
  puts error
  exit 1
end

def check_run(*command)
  system(*command)
  fail_with_error "Running command failed: #{command}" if $CHILD_STATUS.exitstatus != 0
end

def detect_and_setup_local_ref(remote_ref)
  local_heads_ref = "refs/remotes/#{remote}/"

  if pull_request_ref? remote_ref
    if remote_ref.end_with? PULL_REQUEST_REF_SUFFIX
      local_branch = remote_ref[0..(remote_ref.length - 1 - PULL_REQUEST_REF_SUFFIX.length)]
      local_heads_ref += local_branch
    else
      fail_with_error "Unrecognized PR ref: #{remote_ref}"
    end

    check_run 'git', 'fetch', REMOTE, "#{remote_ref}:#{local_brach}"
  else

    if remote_ref.start_with? NORMAL_REF_PREFIX
      local_heads_ref += remote_ref[NORMAL_REF_PREFIX.length..-1]
    else
      fail_with_error "Unrecognized normal ref: #{remote_ref}"
    end

    check_run 'git', 'fetch', REMOTE, remote_ref

  end

  check_run 'git', 'checkout', local_heads_ref, '--force'
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

#
# End of function, start of main code
#

puts 'CI executor script starting'

connect_url = ARGV[0].sub('https://', 'wss://').sub('http://', 'ws://')

fail_with_error 'Build status report URL is empty' if connect_url.nil? || connect_url == ''

# TODO: check repo refs first as we can report that easily here

if DAEMONIZE
  puts 'Daemonizing rest of job running'

  raise 'Fork failed' if (pid = fork) == -1

  exit unless pid.nil?

  Process.setsid

  $stdin.reopen '/dev/null'
  $stdout.reopen 'build_script_output.txt', 'w'
  $stderr.reopen $stdout
end

# Start websocket connection for sending output and then run the build after setup with podman
EM.run {
  ws = Faye::WebSocket::Client.new(connect_url, [], { ping: 55 })

  ws.on :open do |_event|
    puts 'Connected with websocket, sending initial section...'

    send_json(ws, { Type: 'SectionStart', SectionName: 'Test section' })
    send_json(ws, { Type: 'BuildOutput', Output: 'TODO: build output here' })

    # TODO: remove these sends:
    send_json(ws, { Type: 'SectionEnd', WasSuccessful: true })
    send_json(ws, { Type: 'FinalStatus', WasSuccessful: true })

    puts 'Initial section sent'
  end

  ws.on :message do |event|
    puts 'Received websocket message from the server'
    # TODO: implement parsing it
    puts event.data
  end

  ws.on :close do |event|
    puts "Remote side closed websocket, code: #{event.code}, reason: #{event.reason}"
    puts "Client status: #{ws.status}, headers: #{ws.headers}"
    ws = nil
  end

  puts 'TODO: implement doing more stuff here'

  # TODO: setup cache folders
  # TODO: checkout the right ref
  # TODO: run build command and wait for it

  EventMachine.add_timer(10) {
    puts 'Stopping event machine'
    EventMachine.stop_event_loop

    begin
      ws&.close
    rescue StandardError => e
      puts "Failed to close websocket: #{e}"
    end
  }
}

puts 'Exiting CI executor script'
