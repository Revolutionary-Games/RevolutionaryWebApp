#!/usr/bin/env ruby
# Cleans already built binaries for when things don't work
require 'fileutils'

FOLDERS = ['AutomatedUITests', 'CIExecutor', 'Client', 'Client.Tests', 'Server',
           'Server.Common', 'Server.Tests', 'Shared', 'Shared.Tests']

def delete(item)
  if File.exist? item
    puts "Deleting: #{item}"
    FileUtils.rm_r item
  end
end

FOLDERS.each { |folder|
  delete File.join(folder, 'bin')
  delete File.join(folder, 'obj')
}
