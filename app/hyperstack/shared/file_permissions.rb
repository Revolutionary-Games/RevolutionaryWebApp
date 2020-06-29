# frozen_string_literal: true

# Access specifiers
# public - 0
# user - 1
# developer - 2
# owner / admin - 3
# nobody / system - 4
ITEM_ACCESS_PUBLIC = 0
ITEM_ACCESS_USER = 1
ITEM_ACCESS_DEVELOPER = 2
ITEM_ACCESS_OWNER = 3
ITEM_ACCESS_NOBODY = 4

module FilePermissions
  # Returns true if user matches the given required access
  def self.has_access?(user, access_required, owner_id = nil)
    if user.nil?
      access_required == ITEM_ACCESS_PUBLIC
    elsif user.admin?
      access_required < ITEM_ACCESS_NOBODY
    elsif user.developer?
      access_required <= ITEM_ACCESS_DEVELOPER || user.id == owner_id
    else
      access_required <= ITEM_ACCESS_USER || user.id == owner_id
    end
  end
end
