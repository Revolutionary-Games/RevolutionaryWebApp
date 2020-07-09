# frozen_string_literal: true

# Single version of a remote storage item
class StorageItemVersion < ApplicationRecord
  belongs_to :storage_item
  belongs_to :storage_file, optional: true, dependent: :destroy

  validates :version, presence: true, uniqueness: { scope: :storage_item }
  validates :storage_file, uniqueness: true, allow_nil: true
  validates :storage_file, presence: true, if: -> { !uploading }
  validates :keep, inclusion: { in: [true, false] }
  validates :protected, inclusion: { in: [true, false] }
  validates :uploading, inclusion: { in: [true, false] }

  scope :by_storage_item, lambda { |item_id|
    where(storage_item_id: item_id)
  }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  server_method :size_mib, default: '' do
    rounded = 3
    size = storage_file&.size

    if size
      (size.to_f / 1024 / 1024).round(rounded)
    else
      0
    end
  end

  def compute_storage_path
    parent_path = ''
    unless storage_item.parent.nil?
      parent_path = storage_item.parent.compute_storage_path + '/'
    end

    "#{parent_path}#{version}/#{storage_item.name}"
  end

  def create_storage_item(expires, size)
    file = StorageFile.create! storage_path: compute_storage_path, size: size, uploading: true,
                               upload_expires: expires + 1

    self.storage_file = file
    save!

    file
  end
end
