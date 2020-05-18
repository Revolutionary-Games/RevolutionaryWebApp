# frozen_string_literal: true

# Helper methods to make LFS operations consistent accross different places
module LfsHelper
  DOWNLOAD_EXPIRE_TIME = 1800
  UPLOAD_EXPIRE_TIME = 3000

  def create_download_for_lfs_object(object)
    path = '/' + object.storage_path
    expires_at = Time.now.to_i + DOWNLOAD_EXPIRE_TIME

    unhashed_key = ENV['LFS_STORAGE_DOWNLOAD_KEY'] + path + expires_at.to_s

    # IP validation would be added here. unhashed_key += remote ip

    token = Base64.encode64(Digest::MD5.digest(unhashed_key))

    token = token.tr("\n", '').tr('+', '-').tr('/', '_').delete('=')

    url = URI.join(ENV['LFS_STORAGE_DOWNLOAD'],
                   path).to_s + "?token=#{token}&expires=#{expires_at}"

    [url, DOWNLOAD_EXPIRE_TIME]
  end
end
