# frozen_string_literal: true

module FileOps
  # Deletes a folder
  class DeleteFolder < Hyperstack::ServerOp
    param :acting_user
    param :folder_id, type: Integer
    add_error(:folder_id, :does_not_exist, 'folder does not exist') {
      !(@folder = StorageItem.find_by_id(params.folder_id))
    }
    add_error(:folder_id, :is_not_folder, 'given folder id is not a folder') {
      !@folder.folder?
    }
    step {
      raise "Folder is special and can't be deleted" if @folder.special

      unless FilePermissions.has_access? params.acting_user, @folder.write_access, @folder.owner_id
        raise "You don't have permission to delete this folder"
      end

      raise 'Folder is non-empty, delete contents first' if @folder.folder_entries.count > 0
    }
    step {
      @folder.destroy
      Rails.logger.info "Folder (#{@folder.name}) deleted by #{params.acting_user.email}"
    }
  end
end
