# frozen_string_literal: true

# Creates the standard StorageItems if missing
class CreateStandardFoldersIfMissing < ApplicationJob
  queue_as :default

  def perform
    # Trash folder
    StorageItem.find_or_create_by! name: 'Trash', ftype: 1, special: true,
                                   allow_parentless: true, read_access: ITEM_ACCESS_DEVELOPER,
                                   write_access: ITEM_ACCESS_NOBODY, parent_id: nil

    # Devbuilds storage folder
    dev = StorageItem.find_or_create_by! name: 'DevBuild files', ftype: 1, special: true,
                                   allow_parentless: true, read_access: ITEM_ACCESS_USER,
                                   write_access: ITEM_ACCESS_NOBODY, parent_id: nil
    StorageItem.find_or_create_by! name: 'Objects', ftype: 1, special: true,
                                   read_access: ITEM_ACCESS_USER,
                                   write_access: ITEM_ACCESS_NOBODY, parent_id: dev.id
    StorageItem.find_or_create_by! name: 'Dehydrated', ftype: 1, special: true,
                                   read_access: ITEM_ACCESS_USER,
                                   write_access: ITEM_ACCESS_NOBODY, parent_id: dev.id

    # Public folder
    StorageItem.find_or_create_by! name: 'Public', ftype: 1, special: true,
                                   allow_parentless: true, read_access: ITEM_ACCESS_PUBLIC,
                                   write_access: ITEM_ACCESS_DEVELOPER, parent_id: nil
  end
end
