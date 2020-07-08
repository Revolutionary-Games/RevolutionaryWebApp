# frozen_string_literal: true

module FileOps
  # Reports that an upload started with RequestStartUpload is now finished
  class ReportFinishedUpload < Hyperstack::ServerOp
    param acting_user: nil, nils: true
    param :upload_token, type: String
    add_error(:upload_token, :is_blank, 'upload token is blank') {
      params.upload_token.blank?
    }
    step {
      _, @item, @version = RemoteStorageHelper.handle_finished_token params.upload_token
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
end
