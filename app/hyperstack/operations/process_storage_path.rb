# frozen_string_literal: true

# Parses a general remote storage file path
# Return is a list of (list of folders in the path), and selected folder item (if any), error
class ProcessStoragePath < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :path, type: String
  step {
    path = params.path

    path = '/' if path.blank?

    parts = path.split('/').delete_if(&:blank?)

    if parts.empty?
      succeed! [[], nil, ""]
    end

    parent = nil
    folder_path = []
    item = nil

    parts.each{|part|
      begin
        current = StorageItem.by_folder(parent&.id).where(name: part).first
      rescue  StandardError => e
        succeed! [folder_path, nil, "Error accessing '#{part}': #{e}"]
      end

      if current.nil?
        succeed! [folder_path, nil, "Path item '#{part}' doesn't exist"]
      end

      if !FilePermissions.has_access? param.acting_user, current.read_access, current.owner
        succeed! [folder_path, nil, "You are not allowed to view this item"]
      end

      if current.folder?
        folder_path.append current
        parent = current
      else
        item = current
        break
      end
    }

    [folder_path, item, ""]
  }
end
