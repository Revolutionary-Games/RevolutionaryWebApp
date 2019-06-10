module API
  module V1
    class CrashReport < Grape::API
      include API::V1::Defaults
      # use Grape::Attack::Throttle

      resource :crash_report do

        # throttle max: 10, per: 15.seconds

        desc "Store a crash report from a dmp file and additional fields"
        params do
          requires :dump, type: File
          requires :exit_code, type: String
          requires :crash_time, type: Integer
          requires :public, type: Boolean          
          requires :log_files, type: String          
          optional :extra_description, type: String
          optional :email, type: String
          
        end
        post "" do

          time = Time.at(permitted_params[:crash_time]).to_datetime
          
          report = Report.create crash_time: time,
                                 extra_description: permitted_params[:extra_description],
                                 reporter_ip: env["REMOTE_ADDR"],
                                 reporter_email: permitted_params[:email],
                                 log_files: permitted_params[:log_files],
                                 public: permitted_params[:public]

          if report.log_files.blank?
            report.log_files = "no logs provided"
          end

          result, timeout, exit_status = StackwalkPerformer::performStackwalk(
                             permitted_params[:dump][:tempfile].path, timeout: 60);

          if timeout || exit_status != 0
            error!({error_code: 500, error: "Internal Server Error"}, 500)
            return
          end

          if result.empty?
            error!({error_code: 400, error: "Invalid Crashdump file"}, 500)
            return
          end

          report.processed_dump = result

          primary = nil
          foundStart = false
          
          result.each_line{|line|

            if foundStart
              if line.blank?
                break
              end

              primary.concat(line)
            else
              if line =~ /Thread\s+\d+\s+\(crashed\).*/i
                foundStart = true
                primary = line
              end
            end
          }

          if foundStart
            report.primary_callstack = primary
          end

          if !report.primary_callstack
            logger.debug "Failed to detect correct part of primary callstack"
            report.primary_callstack = result[0..400]
          end

          report.description = "Thrive crash (#{permitted_params[:exit_code]})"

          if !report.valid?
            logger.error("validation failures: " + report.errors.full_messages.join('.\n'));
            error!({error_code: 400, error: "Created Report object is invalid"}, 400)
            return
          end
          
          if !report.save
            error!({error_code: 500, error: "Internal Server Error"}, 500)
            return
          end
          
          {"delete_key": report.delete_key, "created_id": report.id}
        end
      end
    end
  end
end
