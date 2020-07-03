# frozen_string_literal: true

namespace :thrive do
  desc 'Refresh all remote object content types'
  task :refresh_remote_content_types, [] => [:environment] do |_task, args|
    StorageItem.find_in_batches{|batch|
      batch.each{|item|
        puts "Updating content-type of #{item.remote_path}"
        RemoteStorageHelper.set_remote_content_type item.remote_path
      }
    }
  end
end
