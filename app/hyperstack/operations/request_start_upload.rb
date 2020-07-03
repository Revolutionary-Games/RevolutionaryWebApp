# frozen_string_literal: true

# Requests from the server permission to upload a file
class RequestStartUpload < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :folder_id, type: Integer, nils: true
  param :size, type: Integer
  param :file_name, type: String
  param :mime_type, type: String
  add_error(:file_name, :is_blank, 'file name is blank') {
    params.file_name.blank?
  }
  add_error(:mime_type, :is_blank, 'mime_type is blank') {
    params.mime_type.blank?
  }
  add_error(:size, :is_invalid, 'file size is invalid') {
    # Max size is 100 GiB
    params.size <= 0 || params.size > MAX_ALLOWED_REMOTE_OBJECT_SIZE
  }
  step { @folder = StorageItem.find_by_id(params.folder_id) }
  step {
    @item = StorageItem.find_by parent_id: @folder&.id, name: params.file_name

    if @item.nil?
      unless FilePermissions.has_access? params.acting_user, if @folder
                                                               @folder.write_access
                                                             else
                                                               ITEM_ACCESS_OWNER
                                                             end, @folder&.owner
        raise "You don't have permission to create files in this folder"
      end

      @item = StorageItem.create!(
        name: params.file_name, ftype: 0, read_access: if @folder
                                                         @folder.read_access
                                                       else
                                                         ITEM_ACCESS_DEVELOPER
                                                       end,
        write_access: @folder ? @folder.write_access : ITEM_ACCESS_OWNER,
        owner: params.acting_user, parent: @folder
      )

      # Item count in folder changed
      CountRemoteFolderItems.perform_later(@folder.id) if @folder
    end

    unless FilePermissions.has_access? params.acting_user, @item.write_access, @item.owner
      raise "You don't have permission to write to the specified file"
    end

    raise "Can't write over a folder" if @item.folder?
    raise "Can't write over a special item" if @item.special
  }
  step {
    version = @item.next_version

    path = version.compute_storage_path

    expires = Time.now + RemoteStorageHelper.upload_expire_time

    file = StorageFile.create! storage_path: path, size: params.size, uploading: true,
                               upload_expires: expires + 1

    version.storage_file = file
    version.save!

    server_mime = RemoteStorageHelper.mime_type @item.name

    if params.mime_type != server_mime
      Rails.logger.info "Client disagrees on mime type, (client) #{params.mime_type} != "\
                        "#{server_mime} (server)"
    end

    presigned_post = RemoteStorageHelper.create_presigned_post file.storage_path,
                                                               params.mime_type
    token = RemoteStorageHelper.create_put_token file.storage_path, file.size, file.id

    [presigned_post.url, presigned_post.fields, token]
  }
end
