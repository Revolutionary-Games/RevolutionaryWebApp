# frozen_string_literal: true

# Creates a new folder
class CreateNewFolder < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :parent_folder_id, type: Integer, nils: true
  param :name, type: String
  param :read_access, type: String
  param :write_access, type: String
  add_error(:name, :is_blank, 'name is blank') {
    params.name.blank?
  }
  add_error(:name, :is_invalid, 'name is too long') {
    params.name.size > 100
  }
  add_error(:name, :has_trailing_or_preceeding_whitespace,
            'name has preceding or trailing whitespace') {
    params.name != params.name.strip
  }
  step {
    # Could maybe in the future allow anonymous folder creation
    raise 'You must be logged in to create folders' unless params.acting_user
  }
  step {
    @parent = params.parent_folder_id ? StorageItem.find_by_id(params.parent_folder_id) : nil
    @read = FilePermissions.parse_access params.read_access
    @write = FilePermissions.parse_access params.write_access
  }
  step {
    if StorageItem.find_by name: params.name, parent_id: @parent ? @parent.id : nil
      raise 'There already exists a folder with the name in the target folder'
    end
  }
  step {
    created = StorageItem.create! name: params.name, parent: @parent || nil, ftype: 1,
                                  special: false, allow_parentless: @parent ? false : true,
                                  read_access: @read, write_access: @write,
                                  owner: params.acting_user

    Rails.logger.info "Folder '#{created.name}' (parent: #{@parent&.name}) " \
                       "created by #{params.acting_user.email}"

    created.id
  }
end
