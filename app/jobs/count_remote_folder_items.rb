# frozen_string_literal: true

# Counts the number of child items for a folder
class CountRemoteFolderItems < ApplicationJob
  queue_as :default

  def perform(item_id)
    item = StorageItem.find item_id

    raise "Item is not folder, can't count items" unless item.folder?

    item.size = item.folder_entries.count
    item.save!
  end
end
