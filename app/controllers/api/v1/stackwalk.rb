module API
  module V1
    class Stackwalk < Grape::API
      include API::V1::Defaults
      # use Grape::Attack::Throttle

      resource :stackwalk do

        # throttle max: 10, per: 15.seconds

        desc "Perform stackwalk on uploaded minidump"
        params do
          requires :data, type: File
        end
        post "" do

          if !permitted_params[:data]
            error!({error_code: 400, error: "Empty file"}, 400)
            return
          end

          result, timeout, exit_status = StackwalkPerformer::performStackwalk(
                             permitted_params[:data][:tempfile].path, timeout: 60);

          if timeout || exit_status != 0
            error!({error_code: 500, error: "Internal Server Error"}, 500)
            return
          end

          content_type "text/plain"
          result
        end
      end
    end
  end
end
