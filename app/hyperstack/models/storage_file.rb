# frozen_string_literal: true

# A stored file in S3 compatible storage
class StorageFile < ApplicationRecord
  before_destroy :destroy_file_in_storage

  validates :storage_path, presence: true, length: { minimum: 3 }, format: { with: %r{[^/].+}i }
  validates :size, presence: true, numericality: { only_integer: true }

  def destroy_file_in_storage
    RemoteStorageHelper.delete_file storage_path
  rescue StandardError => e
    Rails.logger.warn "Failed to delete storage file (#{storage_path}) due to error: #{e}"
  end
end
