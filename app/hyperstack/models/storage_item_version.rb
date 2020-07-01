# frozen_string_literal: true

# Single version of a remote storage item
class StorageItemVersion < ApplicationRecord
  belongs_to :storage_item
  belongs_to :storage_file, optional: true

  before_destroy :destroy_storage_file

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

  def destroy_storage_file
    storage_file&.destroy
  end
end
