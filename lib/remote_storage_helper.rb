# frozen_string_literal: true

require 'aws-sdk-s3'

# Helpers for managing remotely stored files
module RemoteStorageHelper
  GENERAL_UPLOAD_KEY_DERIVE = 'general_upload'
  DOWNLOAD_EXPIRE_TIME = 60.minutes
  UPLOAD_EXPIRE_TIME = 15.minutes

  @already_verified = false
  @verify_was_good = false

  def self.verify
    if !ENV['GENERAL_STORAGE_DOWNLOAD'] || !ENV['GENERAL_STORAGE_DOWNLOAD_KEY'] ||
       !ENV['GENERAL_STORAGE_S3_ENDPOINT'] || !ENV['GENERAL_STORAGE_S3_BUCKET'] ||
       !ENV['GENERAL_STORAGE_S3_REGION'] || !ENV['GENERAL_STORAGE_S3_ACCESS_KEY'] ||
       !ENV['GENERAL_STORAGE_S3_SECRET_KEY'] || !ENV['BASE_URL']
      raise 'Server configuration variables for general remote storage are invalid'
    end
  end

  def self.delete_file(remote_path)
    bucket.object(remote_path).delete
  end

  def self.create_put_url(remote_path)
    bucket.object(remote_path).presigned_url(:put, expires_in: UPLOAD_EXPIRE_TIME + 1)
  end

  def self.object_size(remote_path)
    bucket.object(remote_path).content_length
  end

  def self.create_download_url(remote_path)
    path = '/' + remote_path
    expires_at = Time.now.to_i + DOWNLOAD_EXPIRE_TIME

    URI.join(ENV['LFS_STORAGE_DOWNLOAD'],
             path).to_s + RemoteStorageHelper.sign_bunny_cdn_download_url(
               path, expires_at, ENV['GENERAL_STORAGE_DOWNLOAD_KEY']
             )
  end

  def self.upload_derived_key
    Rails.application.key_generator.generate_key(GENERAL_UPLOAD_KEY_DERIVE)
  end

  def self.bucket
    verify

    aws_client = Aws::S3::Client.new(
      region: ENV['GENERAL_STORAGE_S3_REGION'],
      endpoint: ENV['GENERAL_STORAGE_S3_ENDPOINT'],
      access_key_id: ENV['GENERAL_STORAGE_S3_ACCESS_KEY'],
      secret_access_key: ENV['GENERAL_STORAGE_S3_SECRET_KEY']
    )

    s3 = Aws::S3::Resource.new(client: aws_client)

    result = s3.bucket ENV['GENERAL_STORAGE_S3_BUCKET']

    if !@already_verified
      @already_verified = true
      unless result.exists?
        @verify_was_good = false
        raise 'Target S3 bucket does not exist or configured credentials are wrong'
      end
    else
      raise 'Initial S3 bucket exist check failed' unless @verify_was_good
    end

    result
  end

  def self.sign_bunny_cdn_download_url(path, expires_at, key)
    unhashed_key = key + path + expires_at.to_s

    # IP validation would be added here. unhashed_key += remote ip

    token = Base64.encode64(Digest::MD5.digest(unhashed_key))

    token = token.tr("\n", '').tr('+', '-').tr('/', '_').delete('=')

    "?token=#{token}&expires=#{expires_at}"
  end
end
