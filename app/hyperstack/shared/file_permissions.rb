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

  def self.access_to_string(access)
    case access
    when ITEM_ACCESS_PUBLIC
      'public'
    when ITEM_ACCESS_USER
      'users'
    when ITEM_ACCESS_DEVELOPER
      'developers'
    when ITEM_ACCESS_OWNER
      'owner'
    when ITEM_ACCESS_NOBODY
      'system'
    else
      "Unknown (#{access})"
    end
  end

  def self.parse_access(access)
    case access
    when 'public'
      ITEM_ACCESS_PUBLIC
    when 'users'
      ITEM_ACCESS_USER
    when 'developers'
      ITEM_ACCESS_DEVELOPER
    when 'owner', 'owner + admins', 'admins'
      ITEM_ACCESS_OWNER
    when 'nobody'
      ITEM_ACCESS_NOBODY
    else
      raise "Unknown access specified: #{access}"
    end
  end
end
