#!/usr/bin/env ruby
# frozen_string_literal: true

# Helper script to run dotnet-ef tool

require 'English'
require 'optparse'

@options = {
  install: false,
  update: false,
  create: nil,
  migrate: false,
  remove: false,
  downgrade: nil
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
  opts.on('-m', '--[no-]migrate',
          'Run database migrations against the local database') do |migrate|
    @options[:migrate] = migrate
  end
  opts.on('-r', '--[no-]remove',
          'Remove latest migration (db needs to be down migrated first)') do |remove|
    @options[:remove] = remove
  end
  opts.on('--downgrade MIGRATION',
          'Downgrades the local database to specified migration level ' \
          '(0 to clear entirely)') do |downgrade|
    @options[:downgrade] = downgrade
  end
  opts.on('--recreate MIGRATION',
          'Recreates latest migration. If DB is already migrated '\
          'use "--redo" instead') do |recreate|
    @options[:remove] = true
    @options[:create] = recreate
  end
  opts.on('--redo DOWNGRADE_TO,LATEST_MIGRATION', Array,
          'Down migrates the db and recreates the latest migration') do |again|
    @options[:downgrade] = again[0]
    @options[:remove] = true
    @options[:create] = again[1]
    @options[:migrate] = true
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

if @options[:downgrade]
  puts "Migrating local database back down to version #{@options[:downgrade]}"

  Dir.chdir('Server') do
    system 'dotnet', 'ef', 'database', 'update', @options[:downgrade], '--context',
           'ApplicationDbContext'
  end

  if $CHILD_STATUS.exitstatus != 0
    abort('failed to migrate local database to target migration')
  end
end

if @options[:remove]
  puts 'Removing latest migration'

  Dir.chdir('Server') do
    system 'dotnet', 'ef', 'migrations', 'remove', '--context', 'ApplicationDbContext'
  end

  abort('failed to remove migration') if $CHILD_STATUS.exitstatus != 0
end

if @options[:create]
  puts "Creating migration #{@options[:create]}"

  Dir.chdir('Server') do
    system 'dotnet', 'ef', 'migrations', 'add', @options[:create], '--context',
           'ApplicationDbContext'
  end

  abort('failed to create migration') if $CHILD_STATUS.exitstatus != 0
end

if @options[:migrate]
  puts 'Migrating local database'

  Dir.chdir('Server') do
    system 'dotnet', 'ef', 'database', 'update', '--context', 'ApplicationDbContext'
  end

  abort('failed to update local database') if $CHILD_STATUS.exitstatus != 0
end

puts 'Finished operations'
