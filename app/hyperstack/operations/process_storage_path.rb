# frozen_string_literal: true

# Parses a general remote storage file path
# Return is a list of (list of folders in the path), and selected folder item (if any), error
class ProcessStoragePath < Hyperstack::ServerOp
  param acting_user: nil, nils: true
  param :path, type: String
  step {
    path = params.path

    path = '/' if path.blank?

    path.split('/').delete_if(&:blank?)
  }
  step {|parts|
    if parts.empty?
      return [[], nil, ""]
    end

    parent = nil
    folder_path = []
    item = nil

    parts.each{|part|
      begin
        current = StorageItem.by_folder(parent&.id).where(name: part)
      rescue  StandardError => e
        return [folder_path, nil, "Error accessing '#{part}': #{e}"]
      end

      if current.nil?
        return [folder_path, nil, "Path item '#{part}' doesn't exist"]
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
