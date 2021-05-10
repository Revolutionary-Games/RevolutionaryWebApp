#!/usr/bin/env ruby
# frozen_string_literal: true

# This is the CI executor to run on the build machine for ThriveDevCenter

require 'English'

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

connect_url = ARGV[1]

# TODO: delete to not leak this after things work
puts "build output connect URL: #{connect_url}"

puts 'TODO: implement doing more stuff here'
