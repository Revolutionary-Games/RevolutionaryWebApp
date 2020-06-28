# frozen_string_literal: true

# Single version of a remote storage item
class StorageItemVersion < ApplicationRecord
  belongs_to :storage_item
  has_one :storage_file

  before_destroy :destroy_storage_file

  validates :version, presence: true, uniqueness: { scope: :storage_item }
  validates :storage_file, presence: true, uniqueness: true
  validates :keep, inclusion: { in: [true, false] }
  validates :protected, inclusion: { in: [true, false] }

  scope :by_storage_item, lambda { |item_id|
    where(storage_item_id: item_id)
  }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  def destroy_storage_file
    storage_file&.destroy
  end
end
