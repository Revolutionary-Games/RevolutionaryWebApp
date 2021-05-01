# frozen_string_literal: true

require 'json'
require 'fileutils'
require 'zlib'
require 'digest'
require 'English'

# Fixes all incorrect hashes in a blazor.boot.json file
def fix_boot_json_hashes(path_to_boot)
  data = JSON.parse File.read(path_to_boot)

  changes = process_hash_helper File.dirname(path_to_boot), data

  return unless changes

  puts "Detected invalid hashes in #{path_to_boot} recreating the file"

  # Only write out if we made changes
  File.write path_to_boot, JSON.pretty_generate(data, { indent: '  ' })

  regenerate_compressed_files path_to_boot
end

def process_hash_helper(base_path, value)
  changes = false

  value.each { |key, child_value|
    if child_value.is_a? Hash
      changes = true if process_hash_helper(base_path, child_value)
    elsif child_value.is_a?(String) && child_value.include?('sha256-')
      # Detected a value that contains a hash
      correct = calculate_file_sha256_base64(File.join(base_path, key))

      if correct != child_value
        puts "Invalid hash detected for entry: #{key}"
        value[key] = correct
        changes = true
      end
    end
  }

  changes
end

def regenerate_compressed_files(path)
  gzipped = path + '.gz'
  FileUtils.rm_f gzipped

  Zlib::GzipWriter.open(gzipped, Zlib::BEST_COMPRESSION) { |gz|
    gz.mtime = File.mtime(path)
    gz.orig_name = path
    gz.write IO.binread(path)
  }

  brotlied = path + '.br'
  FileUtils.rm_f brotlied

  # TODO: would it be better to require installing the brotli gem
  # rather than depend on system installed tool?
  system('brotli', '-9ko', brotlied, path)
  raise 'failed to brotli compress' if $CHILD_STATUS.exitstatus != 0
end

def calculate_file_sha256_base64(file)
  sha256 = Digest::SHA256.file file
  "sha256-#{sha256.base64digest}"
end
