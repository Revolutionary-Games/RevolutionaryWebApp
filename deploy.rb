#!/usr/bin/env ruby
# frozen_string_literal: true

# ThriveDevCenter deploy

require 'English'
require 'optparse'

require_relative 'fix_boot_json_hashes'

@options = {
  mode: 'staging',
  # TODO: add running only specific migrations mode
  migration: 'idempotent',
  use_migrations: true,
  use_deploy: true
}

CLIENT_BUILT_WEBROOT = 'Client/bin/Release/net5.0/publish/wwwroot/'
SERVER_BUILT_BASE = 'Server/bin/Release/net5.0/publish/'
CI_EXECUTOR_BUILT_FILE = 'CIExecutor/bin/Release/net5.0/linux-x64/publish/CIExecutor'
CI_EXECUTOR_EXTRA_RESOURCES = ['CIExecutor/bin/Release/net5.0/linux-x64/' \
                              'libMonoPosixHelper.so'].freeze

OptionParser.new do |opts|
  opts.banner = "Usage: #{$PROGRAM_NAME} [options]"

  opts.on('-m', '--mode MODE',
          'Deploy mode. staging or production') do |mode|
    @options[:mode] = mode
  end
  # opts.on('-t', '--migration-type TYPE',
  #         'Type of migration to perform') do |type|
  #   @options[:migration] = type
  # end
  opts.on('--skip-migrations',
          'Skip applying migrations') do |_f|
    @options[:use_migrations] = false
  end
  opts.on('--skip-deploy',
          'Skip actually deploying the code to the server') do |_f|
    @options[:use_deploy] = false
  end
end.parse!

abort "Unhandled parameters: #{ARGV}" unless ARGV.empty?

abort 'Invalid mode' unless %w[staging production].include?(@options[:mode])
abort 'Invalid migration type' unless %w[idempotent].include?(@options[:migration])

TARGET_HOST_WWW_ROOT = "/var/www/thrivedevcenter/#{@options[:mode]}"
TARGET_HOST_APP_ROOT = "/opt/thrivedevcenter/#{@options[:mode]}"

target_host = if @options[:mode] == 'production'
                'dev.revolutionarygamesstudio.com'
              else
                'staging.dev.revolutionarygamesstudio.com'
              end

@db_name = if @options[:mode] == 'production'
             'thrivedevcenter'
           else
             'thrivedevcenter_staging'
           end

puts "Starting deploy to: #{target_host}"

# TODO: send site notification about the impending downtime

def grant_type(type)
  if type == 'SEQUENCES'
    'GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO ' \
    "#{@db_name};"
  else
    "GRANT SELECT, INSERT, UPDATE, DELETE ON ALL #{type} IN SCHEMA public TO " \
    "#{@db_name};"
  end
end

if @options[:use_migrations]
  puts 'Generating migration'

  system('dotnet', 'ef', 'migrations', 'script', '--idempotent', '--project',
         'Server/ThriveDevCenter.Server.csproj', '--context', 'ApplicationDbContext',
         '-o', 'migration.sql')

  abort('failed to create migration') if $CHILD_STATUS.exitstatus != 0

  puts "Please check 'migration.sql' for accuracy, then press enter to continue "\
       '(or CTRL-C to cancel)'

  _ = gets
  puts 'Sending migration to server'

  system('scp', 'migration.sql', "root@#{target_host}:migration.sql")
  abort('failed to copy migration to server') if $CHILD_STATUS.exitstatus != 0

  puts 'Running migration...'

  # TODO: would be nice to only show the output here if failed
  system('ssh', "root@#{target_host}",
         "su - postgres -c \"psql -d #{@db_name}\" < migration.sql")

  abort('failed to run migration') if $CHILD_STATUS.exitstatus != 0

  puts 'Trying to fudge grants...'

  # Here it is assumed that db name is the same as the user name
  system('ssh', "root@#{target_host}",
         "su - postgres -c \"psql -d #{@db_name} -c '#{grant_type 'TABLES'}" \
         "#{grant_type 'SEQUENCES'}'\"")

  abort('grants change failed') if $CHILD_STATUS.exitstatus != 0

  puts 'Migration complete. Building release files'
else
  puts 'Building release files'
end

system('dotnet publish -c Release')

abort('failed to build') if $CHILD_STATUS.exitstatus != 0

puts 'Making sure blazor.boot.json has correct hashes'

Dir.glob("#{CLIENT_BUILT_WEBROOT}**/blazor.boot.json") { |file|
  puts "Checking boot file: #{file}"
  fix_boot_json_hashes file
}

puts 'Build finished. Sending files'

unless @options[:use_deploy]
  puts 'Skipping deploy'
  exit 0
end

# Copy the CI executor to the webroot to be able to serve it
FileUtils.cp CI_EXECUTOR_BUILT_FILE, CLIENT_BUILT_WEBROOT
regenerate_compressed_files File.join(CLIENT_BUILT_WEBROOT, 'CIExecutor')

# And it also needs extra files...
CI_EXECUTOR_EXTRA_RESOURCES.each { |resource|
  FileUtils.cp resource, CLIENT_BUILT_WEBROOT
  regenerate_compressed_files File.join(CLIENT_BUILT_WEBROOT, File.basename(resource))
}

system('rsync', '-hr', CLIENT_BUILT_WEBROOT, "root@#{target_host}:#{TARGET_HOST_WWW_ROOT}",
       '--delete')

abort('failed to send files') if $CHILD_STATUS.exitstatus != 0

# App settings is excluded as it has development environment secrets
system('rsync', '-hr', SERVER_BUILT_BASE, "root@#{target_host}:#{TARGET_HOST_APP_ROOT}",
       '--delete', '--exclude', 'appsettings.Development.json', '--exclude', 'wwwroot')

abort('failed to send files (server)') if $CHILD_STATUS.exitstatus != 0

puts 'Files synced to server'

puts 'Deploy finished. Restarting services on the server'

if @options[:mode] == 'production'
  system('ssh', "root@#{target_host}", 'systemctl restart thrivedevcenter')
else
  system('ssh', "root@#{target_host}", 'systemctl restart thrivedevcenter-staging')
end

abort('failed to restart services') if $CHILD_STATUS.exitstatus != 0
