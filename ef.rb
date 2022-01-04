#!/usr/bin/env ruby
# frozen_string_literal: true

# Helper script to run dotnet-ef tool

require 'English'
require 'optparse'

@options = {
  install: false,
  update: false,
  create: nil
}

SERVER_PROJECT_FILE = 'Server/ThriveDevCenter.Server.csproj'
ASP_NET_VERSION_REGEX = /Include="Microsoft\.AspNetCore\..+" Version="([0-9.]+)"/.freeze

OptionParser.new do |opts|
  opts.banner = "Usage: #{$PROGRAM_NAME} [options]"

  opts.on('-i', '--[no-]install',
          'Install the tool') do |install|
    @options[:install] = install
  end
  opts.on('-u', '--[no-]update',
          'Update the tool') do |update|
    @options[:update] = update
  end
  opts.on('-c', '--create MIGRATION_NAME',
          'Create a new migration') do |create|
    @options[:create] = create
  end
end.parse!

abort "Unhandled parameters: #{ARGV}" unless ARGV.empty?

def detect_version
  File.foreach(SERVER_PROJECT_FILE) do |line|
    match = line.match(ASP_NET_VERSION_REGEX)

    if match
      puts "Detected wanted aspnet version: #{match[1]}"
      return match[1]
    end
  end

  abort('could not detect wanted aspnet version')
end

if @options[:install]
  puts 'Installing dotnet-ef tool'
  system 'dotnet', 'tool', 'install', '--global', 'dotnet-ef', '--version', detect_version

  abort('failed to install') if $CHILD_STATUS.exitstatus != 0
end

if @options[:update]
  puts 'Installing dotnet-ef tool'
  system 'dotnet', 'tool', 'update', '--global', 'dotnet-ef', '--version', detect_version

  abort('failed to update') if $CHILD_STATUS.exitstatus != 0
end

puts 'Finished operations'
