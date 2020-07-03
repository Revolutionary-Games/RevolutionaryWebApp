# frozen_string_literal: true

namespace :thrive do
  # Doesn't actually work with the v3 aws API
  desc 'Refresh all remote object content types'
  task :refresh_remote_content_types, [] => [:environment] do |_task, _args|
    StorageItem.find_in_batches { |batch|
      batch.each { |item|
        item.storage_item_versions.each { |version|
          next unless version.storage_file

          puts "Updating content-type of #{version.storage_file.storage_path}"
          RemoteStorageHelper.set_remote_content_type version.storage_file.storage_path
        }
      }
    }
  end

  task :list_content_types, [] => [:environment] do |_task, _args|
    StorageItem.find_in_batches { |batch|
      batch.each { |item|
        item.storage_item_versions.each { |version|
          unless version.storage_file
            puts "Version #{version.version} of file #{item.name} doesn't have a remote file"
            next
          end

          next if version.storage_file.uploading

          puts("Content-type of #{version.storage_file.storage_path}: " +
                 RemoteStorageHelper.content_type(version.storage_file.storage_path).to_s)
        }
      }
    }
  end
end
