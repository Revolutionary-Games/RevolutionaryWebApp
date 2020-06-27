# frozen_string_literal: true

# Helper methods to make LFS operations consistent accross different places
module LfsHelper
  DOWNLOAD_EXPIRE_TIME = 1800
  UPLOAD_EXPIRE_TIME = 3000

  def self.create_download_for_lfs_object(object)
    path = '/' + object.storage_path
    expires_at = Time.now.to_i + DOWNLOAD_EXPIRE_TIME

    url = URI.join(ENV['LFS_STORAGE_DOWNLOAD'],
                   path).to_s + RemoteStorageHelper.sign_bunny_cdn_download_url(
                     path, expires_at, ENV['LFS_STORAGE_DOWNLOAD_KEY']
                   )

    [url, DOWNLOAD_EXPIRE_TIME]
  end
end
