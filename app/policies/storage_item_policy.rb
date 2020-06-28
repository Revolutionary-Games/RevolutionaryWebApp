# frozen_string_literal: true

# Send general storage items to people who can view them
class StorageItemPolicy
  regulate_broadcast { |policy|
    # Owners get their folder data
    policy.send_all.to(owner) if owner

    if read_access <= ITEM_ACCESS_PUBLIC
      policy.send_all_but(:owner).to(Hyperstack::Application)
    elsif read_access <= ITEM_ACCESS_USER
      policy.send_all.to(User)
    elsif read_access <= ITEM_ACCESS_DEVELOPER
      policy.send_all.to(DeveloperUser)
    elsif read_access <= ITEM_ACCESS_OWNER
      policy.send_all.to(AdminUser)
    end
  }
end
