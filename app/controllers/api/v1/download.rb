# frozen_string_literal: true

module API
  module V1
    # Remote file download API
    class Download < Grape::API
      include API::V1::Defaults

      helpers do
      end

      resource :download do
        desc 'Downloads specified item'
        params do
          requires :id, type: Integer, desc: 'Object id'
          optional :version, type: Integer, desc: 'Object version to download'
          optional :api_token, type: String, desc: 'API token'
        end
        get ':id' do
          user = ApiHelper.acting_user_from_session_id cookies['sessions']

          if permitted_params[:api_token]
            user = ApiHelper.get_user_from_api_token permitted_params[:api_token]

            error!({ error_code: 401, error_message: 'Unauthorized.' }, 401) unless user
          end

          item = StorageItem.find_by_id permitted_params[:id]

          if !item || !FilePermissions.has_access?(user, item.read_access, item.owner_id)
            error!({ error_code: 404,
                     error_message: "Item doesn't exist or you need to login to access it." },
                   404)
          end

          if permitted_params[:version]

            use_version = item.storage_item_versions.where(
              version: permitted_params[:version]
            ).first

            if use_version != item.latest_uploaded && !user
              error!({ error_code: 401, error_message:
                'You must login to access non latest version of this object' }, 401)
            end
          else
            use_version = item.latest_uploaded
          end

          unless use_version
            error!({ error_code: 404, error_message: 'Object version not found' }, 404)
          end

          unless use_version.storage_file
            error!({ error_code: 404, error_message:
              'Object version has no associated storage item' }, 404)
          end

          redirect RemoteStorageHelper.create_download_url(
            use_version.storage_file.storage_path
          )
        end
      end
    end
  end
end
