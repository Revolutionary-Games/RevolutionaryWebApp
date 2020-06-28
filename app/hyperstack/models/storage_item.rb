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

# Item in general remote storage, can be a folder (that doesn't actually exist there),
# or a file based on type
class StorageItem < ApplicationRecord
  belongs_to :owner, class_name: 'User', required: false
  belongs_to :parent, class_name: 'StorageItem', required: false
  has_many :folder_entries, class_name: 'StorageItem', foreign_key: 'parent_id'
  has_many :storage_item_versions

  before_destroy :destroy_all_versions, :destroy_child_items

  validates :size, presence: false, numericality: { only_integer: true }, allow_nil: true
  validates :name, presence: true, length: { maximum: 255, minimum: 1 },
                   uniqueness: { scope: :parent }
  validates :ftype, presence: true, inclusion: { in: [0, 1] }
  validates :special, inclusion: { in: [true, false] }
  validates :allow_parentless, inclusion: { in: [true, false] }

  # See the access specifiers at the top of this file
  validates :read_access, presence: true, numericality: { only_integer: true }, inclusion: 0..4
  validates :write_access, presence: true, numericality: { only_integer: true },
                           inclusion: 0..4

  scope :by_folder, lambda { |folder_id|
    if !folder_id.nil?
      where(parent_id: folder_id)
    else
      where(parent_id: nil)
    end
  }

  scope :visible_to, lambda { |id|
    user = !id.nil? ? User.find(id) : nil

    return where(read_access: ITEM_ACCESS_PUBLIC) if user.nil?

    if !user.developer?
      where('read_access <= ? OR owner_id = ?', ITEM_ACCESS_USER, user.id)
    elsif user.admin?
      where('read_access <= ? OR owner_id = ?', ITEM_ACCESS_OWNER, user.id)
    else
      where('read_access <= ? OR owner_id = ?', ITEM_ACCESS_DEVELOPER, user.id)
    end
  }

  scope :sort_by_name,
        server: -> { order('name ASC') },
        select: -> { sort_by(&:name) }

  # TODO: client scope
  scope :folder_sort, -> { order('ftype DESC, name ASC') }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  def folder?
    ftype == 1
  end

  def file?
    ftype == 0
  end

  def size_readable
    if folder?
      size.to_s + ' item'.pluralize(size)
    else
      size_mib.to_s + ' MiB'
    end
  end

  def size_mib(rounded: 3)
    if size
      (size.to_f / 1024 / 1024).round(rounded)
    else
      0
    end
  end

  def read_access_pretty
    StorageItem.access_integer_to_string read_access
  end

  def destroy_all_versions
    storage_item_versions.each(&:destroy)
  end

  def destroy_child_items
    folder_entries.each(&:destroy)
  end

  def self.access_integer_to_string(access)
    case access
    when ITEM_ACCESS_PUBLIC
      'public'
    when ITEM_ACCESS_USER
      'users'
    when ITEM_ACCESS_DEVELOPER
      'developers'
    when ITEM_ACCESS_OWNER
      'owner + admins'
    when ITEM_ACCESS_NOBODY
      'system'
    else
      "Unknown (#{access})"
    end
  end
end
