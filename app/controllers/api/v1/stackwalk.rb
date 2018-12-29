require 'open3'

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

          Open3.popen3("StackWalk/minidump_stackwalk", permitted_params[:data][:tempfile].path,
                       "SymbolData") {|stdin, stdout, stderr, wait_thr|

            result = ""

            outThread = Thread.new{
              stdout.each {|line|
                result.concat(line)
              }
            }

            errThread = Thread.new{
              stderr.each {|line|
              }
            }

            # Handle timeouts
            if wait_thr.join(15) == nil

              logger.error "Stackwalking took more than 15 seconds"
              Process.kill("TERM", wait_thr.pid)
              error!({error_code: 500, error_message: "Internal Server Error"}, 500)
              outThread.kill
              errThread.kill
              return
            end
            
            exit_status = wait_thr.value

            if exit_status != 0
              logger.error "Stackwalking exited with error code"
              error!({error_code: 500, error_message: "Internal Server Error"}, 500)
              return
            end

            logger.debug "Stackwalking succeeded"
            outThread.join
            errThread.join
            content_type "text/plain"
            result
          }
        end
      end
    end
  end
end
