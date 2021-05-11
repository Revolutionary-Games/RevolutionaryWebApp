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

puts 'CI executor script starting'

connect_url = ARGV[0].sub('https://', 'wss://').sub('http://', 'ws://')

fail_with_error 'Build status report URL is empty' if connect_url.nil? || connect_url == ''

# TODO: delete to not leak this after things work
puts "build output connect URL: #{connect_url}"

puts 'Daemonizing rest of job running'

raise 'Fork failed' if (pid = fork) == -1

exit unless pid.nil?

Process.setsid

$stdin.reopen '/dev/null'
$stdout.reopen 'build_script_output.txt', 'w'
$stderr.reopen $stdout

# Now run the rest of the job

puts 'testing websockets...'

EM.run {
  ws = Faye::WebSocket::Client.new(connect_url, ping: 55)

  def send_json(obj)
    as_str = json.dumps(obj)
    # Size first
    ws.send([as_str.length].pack('<l').bytes)

    # Then the data
    ws.send(as_str)
  end

  ws.on :open do |_event|
    puts 'Connected with websocket, sending initial section'

    send_json({ Type: 'SectionStart', SectionName: 'Test section' })
    send_json({ Type: 'BuildOutput', Output: 'TODO: build output here' })
    send_json({ Type: 'SectionEnd', WasSuccessful: true })
    send_json({ Type: 'FinalStatus', WasSuccessful: true })
  end

  ws.on :message do |event|
    puts 'Received websocket message from the server'
    # TODO: implement parsing it
    puts event.data
  end

  ws.on :close do |_event|
    puts 'Remote side closed websocket'
    ws = nil
  end

  puts 'TODO: implement doing more stuff here'

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
