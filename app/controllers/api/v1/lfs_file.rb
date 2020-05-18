# frozen_string_literal: true

module API
  module V1
    # Single LFS file getting endpoint that allows browsers
    class LFSFile < Grape::API
      include API::V1::Defaults
      include ApplicationHelper
      include ApiHelper
      include LfsHelper

      resource :lfs_file do
        desc 'Single LFS file downloading endpoint'
        params do
          requires :project, type: Integer, desc: 'LFS project id'
          requires :path, type: String, desc: 'Path to the LFS file within the project'
          requires :name, type: String, desc: 'Name of the file to download at the path'
          optional :api_token, type: String, desc: 'API token'
        end
        get '' do
          # user = acting_user_from_session({})
          user = nil

          if permitted_params[:api_token]
            user = get_user_from_api_token permitted_params[:api_token]

            error!({ error_code: 401, error_message: 'Unauthorized.' }, 401) unless user
          end

          project = LfsProject.find_by_id permitted_params[:project]

          if !project || (!project.public && !user&.developer?)
            error!({ error_code: 404, error_message: 'Invalid project specified, ' \
                                     "or you don't have access" }, 404)
          end

          # Access is allowed

          # Find file
          file = ProjectGitFile.find_by lfs_project_id: project.id,
                                        name: permitted_params[:name],
                                        path: permitted_params[:path]

          error!({ error_code: 404, error_message: 'File not found' }, 404) unless file

          unless file.lfs?
            error!({ error_code: 400,
                     error_message: 'File is non-lfs file' }, 400)
          end

          object = LfsObject.find_by lfs_project_id: project.id, oid: file.lfs_oid

          unless object
            error!({ error_code: 404, error_message: "Specified LFS object doesn't exist" },
                   404)
          end

          # Redirect to download url
          url, _expires_in = create_download_for_lfs_object object
          redirect_to url
        end
      end
    end
  end
end
