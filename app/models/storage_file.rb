# A stored file in S3 compatible storage
class StorageFile < ApplicationRecord
  validates :storage_path, presence: true, length: { minimum: 3 }
  validates :size, presence: true, numericality: { only_integer: true }
end
