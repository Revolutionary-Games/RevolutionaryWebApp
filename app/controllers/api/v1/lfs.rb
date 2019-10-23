# frozen_string_literal: true

module API
  module V1
    class LFS < Grape::API
      include API::V1::Defaults

      resource :lfs do
        desc 'Git LFS endpoints'
        get '' do
          { error: 'no project specified' }
        end

        params do
          optional :lfs_token, type: String
        end
        get ':slug' do
          user = nil

          if permitted_params[:lfs_token]
            user = User.find_by lfs_token: permitted_params[:lfs_token]
            unless user
              error!({ error_code: 403, error: 'Invalid LFS token' }, 403)
              return
            end
          end

          project = LfsProject.find_by slug: params[:slug]

          if !project || (!project.public? && !user&.developer?)

            error!({ error_code: 404, error: 'No project found' }, 404)
            return
          end

          { id: project.id, slug: project.slug, name: project.name }
        end
      end
    end
  end
end
