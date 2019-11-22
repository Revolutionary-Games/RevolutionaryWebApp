# frozen_string_literal: true

module API
  module V1
    # Basic StackWalk delegating API
    class Stackwalk < Grape::API
      include API::V1::Defaults
      # use Grape::Attack::Throttle

      resource :stackwalk do
        # throttle max: 10, per: 15.seconds

        desc 'Perform stackwalk on uploaded minidump'
        params do
          requires :data, type: File
        end
        post '' do
          error!({ error_code: 400, error: 'Empty file' }, 400) unless permitted_params[:data]

          result, is_error = StackwalkPerformer.perform_stackwalk(
            permitted_params[:data][:tempfile].path, timeout: 60
          )

          if is_error
            error!({ error_code: 500, error: 'Calling StackWalk service failed',
                     message: result }, 500)
          end

          content_type 'text/plain'
          result
        end
      end
    end
  end
end
