# frozen_string_literal: true

# File version info, uses same logic as the storage item
class StorageItemVersionPolicy
  regulate_broadcast { |policy|
    # Orphan
    if !storage_item
      policy.send_all.to(AdminUser)
      next
    end

    # Duplicate code from StorageItem
    # Owners get their folder data
    policy.send_all.to(storage_item.owner) if storage_item.owner

    if storage_item.read_access <= ITEM_ACCESS_PUBLIC
      policy.send_all_but(:owner).to(Hyperstack::Application)
    elsif storage_item.read_access <= ITEM_ACCESS_USER
      policy.send_all.to(User)
    elsif storage_item.read_access <= ITEM_ACCESS_DEVELOPER
      policy.send_all.to(DeveloperUser)
    elsif storage_item.read_access <= ITEM_ACCESS_OWNER
      policy.send_all.to(AdminUser)
    end
  }
end
