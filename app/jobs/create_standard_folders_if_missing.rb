# frozen_string_literal: true

# Creates the standard StorageItems if missing
class CreateStandardFoldersIfMissing < ApplicationJob
  queue_as :default

  def perform
    count_items = false

    # Trash folder
    trash = StorageItem.find_or_create_by!(
      name: 'Trash', ftype: 1, special: true,
      allow_parentless: true, read_access: ITEM_ACCESS_DEVELOPER,
      write_access: ITEM_ACCESS_NOBODY, parent_id: nil
    ) do |_|
      count_items = true
    end

    # Devbuilds storage folder
    dev = StorageItem.find_or_create_by!(
      name: 'DevBuild files', ftype: 1, special: true,
      allow_parentless: true, read_access: ITEM_ACCESS_USER,
      write_access: ITEM_ACCESS_NOBODY, parent_id: nil
    ) do |_|
      count_items = true
    end
    objects = StorageItem.find_or_create_by!(
      name: 'Objects', ftype: 1, special: true,
      read_access: ITEM_ACCESS_USER,
      write_access: ITEM_ACCESS_NOBODY, parent_id: dev.id
    ) do |_|
      count_items = true
    end
    dehydrated = StorageItem.find_or_create_by!(
      name: 'Dehydrated', ftype: 1, special: true,
      read_access: ITEM_ACCESS_USER,
      write_access: ITEM_ACCESS_NOBODY, parent_id: dev.id
    ) do |_|
      count_items = true
    end

    # Public folder
    public = StorageItem.find_or_create_by!(
      name: 'Public', ftype: 1, special: true,
      allow_parentless: true, read_access: ITEM_ACCESS_PUBLIC,
      write_access: ITEM_ACCESS_DEVELOPER, parent_id: nil
    ) do |_|
      count_items = true
    end

    return unless count_items

    # Queue jobs to count items in the folders
    CountRemoteFolderItems.perform_later(trash.id)
    CountRemoteFolderItems.perform_later(dev.id)
    CountRemoteFolderItems.perform_later(objects.id)
    CountRemoteFolderItems.perform_later(dehydrated.id)
    CountRemoteFolderItems.perform_later(public.id)
  end
end
