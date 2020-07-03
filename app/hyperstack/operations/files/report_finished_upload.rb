# frozen_string_literal: true

# Reports that an upload started with RequestStartUpload is now finished
class ReportFinishedUpload < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :upload_token, type: String
  add_error(:upload_token, :is_blank, 'upload token is blank') {
    params.upload_token.blank?
  }
  step {
    # Decode given token
    begin
      @data = RemoteStorageHelper.decode_put_token params.upload_token
    rescue JWT::DecodeError
      raise 'Invalid upload token given'
    end
  }
  step {
    @item = StorageFile.find_by_id @data['item_id']

    raise 'Invalid upload token given' if @item.nil?

    unless RemoteStorageHelper.token_matches_item? @data, @item
      Rails.logger.warn "Token that doesn't match storage item attempted to be used"
      raise 'Invalid upload token given'
    end
  }
  step {
    raise "Can't use token on item that is already marked as uploaded" unless @item.uploading

    @version = StorageItemVersion.find_by storage_file_id: @item.id

    if @version.nil? || @version.storage_item.nil?
      raise 'Specified StorageFile has no associated item version object'
    end

    unless @version.uploading
      raise "Can't use token on item that has already uploaded version"
    end
  }
  step {
    # Verify that upload to S3 was successful
    begin
      size = RemoteStorageHelper.object_size @item.storage_path
    rescue RuntimeError => e
      raise "Checking for item in S3 storage failed: #{e}"
    end

    if size != @item.size
      raise "File size in storage doesn't match reported. #{size} != #{@item.size}"
    end
  }
  step {
    Rails.logger.info "StorageFile (#{@item.storage_path}) is now uploaded by " +
                      (params.acting_user ? params.acting_user.email : 'non-logged in')
    # TODO: add user that uploaded the version to the model
    @item.uploading = false
    @version.uploading = false
    @version.storage_item.size = @item.size
    @item.save!
    @version.save!
    @version.storage_item.save!
    true
  }
end
