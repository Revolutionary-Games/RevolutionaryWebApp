# frozen_string_literal: true

require 'aws-sdk-s3'
require 'sha3'
require 'zlib'

MAX_ALLOWED_REMOTE_OBJECT_SIZE = 1024 * 1024 * 1024 * 100

# Helpers for managing remotely stored files
module RemoteStorageHelper
  GENERAL_UPLOAD_KEY_DERIVE = 'general_upload'
  DOWNLOAD_EXPIRE_TIME = 60.minutes
  UPLOAD_EXPIRE_TIME = 15.minutes

  @already_verified = false

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
    bucket.object(remote_path).presigned_url(:put, expires_in:
      (UPLOAD_EXPIRE_TIME + 1).to_i)
  end

  def self.create_presigned_post(remote_path, client_mime_type)
    bucket.object(remote_path).presigned_post(
      signature_expiration: Time.now + UPLOAD_EXPIRE_TIME + 1, key: remote_path,
      content_type: client_mime_type,
      content_length_range: 1..MAX_ALLOWED_REMOTE_OBJECT_SIZE
    )
  end

  def self.upload_expire_time
    UPLOAD_EXPIRE_TIME
  end

  def self.create_put_token(remote_path, size, item_id, custom = {})
    payload = { remote_path: remote_path, object_size: size,
                item_id: item_id,
                custom: custom,
                exp: Time.now.to_i + UPLOAD_EXPIRE_TIME + 2 }

    JWT.encode payload, upload_derived_key, 'HS256'
  end

  def self.decode_put_token(token)
    decoded_token = JWT.decode token, upload_derived_key, true, algorithm: 'HS256'

    decoded_token[0]
  end

  # Handles a token that client has reported as uploaded
  def self.handle_finished_token(token)
    # Decode given token
    begin
      data = RemoteStorageHelper.decode_put_token token
    rescue JWT::DecodeError
      raise 'Invalid token'
    end

    item = StorageFile.find_by_id data['item_id']

    raise 'Invalid token' if item.nil?

    unless token_matches_item? data, item
      Rails.logger.warn "Token that doesn't match storage item attempted to be used"
      raise 'Invalid token'
    end

    raise "Can't use token on item that is already marked as uploaded" unless item.uploading

    version = StorageItemVersion.find_by storage_file_id: item.id

    if version.nil? || version.storage_item.nil?
      raise 'Specified StorageFile has no associated item version object'
    end

    raise "Can't use token on item that has already uploaded version" unless version.uploading

    # Verify that upload to S3 was successful
    begin
      size = object_size item.storage_path
    rescue RuntimeError => e
      raise "Checking for item in S3 storage failed: #{e}"
    end

    if size != item.size
      raise "File size in storage doesn't match reported. #{size} != #{item.size}"
    end

    [data, item, version]
  end

  def self.token_matches_item?(token_data, item)
    token_data['remote_path'] == item.storage_path && token_data['object_size'] == item.size &&
      token_data['item_id'] == item.id
  end

  def self.object_size(remote_path, b = bucket)
    b.object(remote_path).content_length
  end

  def self.content_type(remote_path, b = bucket)
    b.object(remote_path).content_type
  end

  def self.calculate_ungzipped_hash(remote_path, b = bucket)
    s = SHA3::Digest::SHA256.new

    gz = Zlib::GzipReader.new(b.object(remote_path).get.body)

    until gz.eof?
      s.update gz.read 1024*1024
    end

    s.hexdigest
  end

  # This does not work. Seems like the v2 sdk would would have this working
  def self.set_remote_content_type(remote_path, b = bucket)
    object = b.object(remote_path)
    object.copy_from(object, content_type: MIME::Types.type_for(
      remote_path
    ).first.content_type)
  rescue StandardError => e
    Rails.logger.error "Could not set content type of remote #{remote_path}': #{e}"
  end

  def self.create_download_url(remote_path)
    path = '/' + remote_path
    unescaped_path = path
    path = CGI.escape(path).gsub('%2F', '/').gsub('+', ' ')

    expires_at = Time.now.to_i + DOWNLOAD_EXPIRE_TIME

    (ENV['GENERAL_STORAGE_DOWNLOAD'].chomp('/') + path) +
      RemoteStorageHelper.sign_bunny_cdn_download_url(
        unescaped_path, expires_at, ENV['GENERAL_STORAGE_DOWNLOAD_KEY']
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

    unless @already_verified
      if !result.exists?
        raise 'Target S3 bucket does not exist or configured credentials are wrong'
      else
        @already_verified = true
      end
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

  def self.mime_type(path)
    MIME::Types.type_for(path).first.content_type
  rescue StandardError
    'binary/octet_stream'
  end
end
