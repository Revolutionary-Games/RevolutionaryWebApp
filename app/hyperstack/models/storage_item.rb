# frozen_string_literal: true

# Item in general remote storage, can be a folder (that doesn't actually exist there),
# or a file based on type
class StorageItem < ApplicationRecord
  belongs_to :owner, class_name: 'User', required: false
  belongs_to :parent, class_name: 'StorageItem', required: false
  has_many :folder_entries, class_name: 'StorageItem', foreign_key: 'parent_id',
                            dependent: :destroy
  has_many :storage_item_versions, -> { order('version DESC') }, primary_key: 'id',
           dependent: :destroy

  validates :size, presence: false, numericality: { only_integer: true }, allow_nil: true
  validates :name, presence: true, length: { maximum: 255, minimum: 1 },
                   uniqueness: { scope: :parent }
  validates :ftype, presence: true, inclusion: { in: [0, 1] }
  validates :special, inclusion: { in: [true, false] }
  validates :allow_parentless, inclusion: { in: [true, false] }

  # See the access specifiers in file_permissions.rb
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
    FilePermissions.access_to_string read_access
  end

  def write_access_pretty
    FilePermissions.access_to_string write_access
  end

  def ftype_pretty
    case ftype
    when 0
      'file'
    when 1
      'folder'
    else
      'unknown'
    end
  end

  def compute_storage_path
    if parent
      parent.compute_storage_path + '/'
    else
      ''
    end + name.to_s
  end

  # TODO: convert to property
  def important
    false
  end

  def latest_uploaded
    storage_item_versions.where(uploading: false).first
  end

  def highest_version
    storage_item_versions.first
  end

  def next_version
    current_highest = highest_version

    version = current_highest ? current_highest.version + 1 : 1;

    storage_item_versions.create! version: version, keep: important, uploading: true,
                                  storage_item: self
  end
end
