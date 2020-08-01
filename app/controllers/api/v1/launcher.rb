# frozen_string_literal: true

module API
  module V1
    # Single LFS file getting endpoint that allows browsers
    class Launcher < Grape::API
      include API::V1::Defaults

      MAX_DEHYDRATED_DOWNLOAD_BATCH = 100
      MAX_PAGE_SIZE = 100

      helpers do
        def suspend_message(user)
          'Your account is suspended' +
            (user.suspended_manually ? ' manually' : ' automatically') +
            ": #{user.suspended_reason}"
        end

        def fetch_link_code
          code = headers['Authorization']

          if !code || code.blank?
            error!({ error_code: 401, message: 'Invalid authorization code' }, 401)
          end

          code
        end

        def active_code
          code = fetch_link_code

          link = LauncherLink.find_by link_code: code

          error!({ error_code: 401, message: 'Invalid authorization code' }, 401) unless link

          if link.user.suspended
            error!({ error_code: 401, message: suspend_message(link.user) }, 403)
          end

          Rails.logger.info "Launcher link of user (#{link.user.email}) " \
                            "used from #{request.ip}"

          link.last_connection = Time.now
          link.total_api_calls += 1
          link.last_ip = request.ip
          Rails.logger.warn "Couldn't save launcher link details" unless link.save

          [link, link.user]
        end

        def check_new_link_code(link_code)
          user = User.find_by launcher_link_code: link_code

          error!({ error_code: 401, message: 'Invalid authorization code' }, 401) unless user

          error!({ error_code: 403, message: suspend_message(user) }, 403) if user.suspended

          if user.launcher_code_expires < Time.now
            error!({ error_code: 403, message:
              'The authorization code has already expired, please get a new one' }, 403)
          end

          if user.launcher_links.count >= DEFAULT_MAX_LAUNCHER_LINKS
            error!({ error_code: 400, message:
              'You have already linked the maximum number of launchers to your account' }, 400)
          end

          user
        end

        def get_download_with_sha3(user, sha3)
          object = DehydratedObject.find_by sha3: sha3

          unless object&.storage_item
            error!({ error_code: 404,
                     message: "The specified object (#{sha3}) wasn't found" }, 404)
          end

          item = object.storage_item

          unless FilePermissions.has_access?(user, item.read_access, item.owner_id)
            error!({ error_code: 403,
                     error_message: "You don't have permission to access this " \
                                    "object's (#{sha3}) download file." },
                   403)
          end

          version = item.latest_uploaded

          if !version || !version.storage_file
            error!(
              {
                error_code: 404,
                message:
                  "The specified object's (#{sha3}) storage doesn't have a valid uploaded file"
              }, 404
            )
          end

          {
            download_url: RemoteStorageHelper.create_download_url(
              version.storage_file.storage_path
            ),
            sha3: sha3
          }
        end
      end

      resource :launcher do
        desc "Checks if launcher link code is valid, doesn't consume the code"
        params do
        end
        get 'check_link' do
          link_code = fetch_link_code

          user = check_new_link_code link_code

          { valid: true, username: user.name, email: user.email, developer: user.developer? }
        end

        desc 'Connects launcher'
        params do
        end
        post 'link' do
          link_code = fetch_link_code

          user = check_new_link_code link_code

          # Update user to consume the code
          user.launcher_code_expires = Time.now - 1
          user.launcher_link_code = nil
          user.total_launcher_links += 1
          user.save!

          # Create a new code, which the user doesn't directly see to avoid it leaking as
          # easily
          new_code = SecureRandom.base58(42)

          LauncherLink.create! user: user, link_code: new_code, last_ip: request.ip,
                               total_api_calls: 0

          { connected: true, code: new_code }
        end

        desc 'Checks launcher connection status'
        params do
        end
        get 'status' do
          link, user = active_code

          { connected: true, username: user.name, email: user.email, developer: user.developer? }
        end

        desc 'Disconnects the current code'
        params do
        end
        delete 'status' do
          link, user = active_code

          Rails.logger.info "Launcher link #{link.link_code} disconnected through " \
                            "API by: #{request.ip}"

          link.destroy!

          { success: true }
        end

        desc 'Downloads a devbuild'
        params do
          optional :build_id, type: Integer, desc: 'Build ID'
        end
        get 'builds/download/:build_id' do
          link, user = active_code

          build = DevBuild.find permitted_params[:build_id]

          unless build.storage_item
            error!({ error_code: 404,
                     message: "The specified build doesn't have a valid download file" }, 404)
          end

          item = build.storage_item

          unless FilePermissions.has_access?(user, item.read_access, item.owner_id)
            error!({ error_code: 403,
                     error_message: "You don't have permission to access this " \
                                    "build's download file." },
                   403)
          end

          version = item.latest_uploaded

          if !version || !version.storage_file
            error!(
              {
                error_code: 404,
                message: "The specified build's storage doesn't have a valid uploaded file"
              }, 404
            )
          end

          build.downloads += 1
          unless build.save
            Rails.logger.warn "Couldn't increment download count for build #{build.id}"
          end

          {
            download_url: RemoteStorageHelper.create_download_url(
              version.storage_file.storage_path
            ),
            dl_hash: build.build_zip_hash
          }
        end

        desc 'Gets currently available devbuild information (latest builds + BOTD)'
        params do
          optional :platform, type: String, desc: 'Platform to search a builds for'
          optional :offset, type: Integer, desc: 'Offset for results'
          optional :page_size, type: Integer, desc: 'Page size for results, max: ' +
                                                    MAX_PAGE_SIZE.to_s
        end
        post 'builds' do
          offset = permitted_params[:offset] || 0
          page_size = permitted_params[:page_size] || MAX_PAGE_SIZE

          if offset < 0 || page_size <= 0
            error!({ error_code: 400, message: 'Invalid offset or page size' }, 400)
          end

          link, user = active_code

          scope = DevBuild.sort_by_created_at

          if permitted_params[:platform]
            scope = scope.where(platform: permitted_params[:platform])
          end

          scope = scope.paginated offset, page_size + 1

          status 200
          items = scope.to_a
          result = { result: items}

          if items.size > page_size
            items.pop
            result[:next_offset] = offset + items.size
          end

          result
        end

        desc 'Searches for a devbuild based on the commit hash'
        params do
          requires :devbuild_hash, type: String, desc: 'Thing to search for'
          optional :platform, type: String, desc: 'Platform to search a build for'
        end
        post 'search' do
          link, user = active_code

          scope = DevBuild.where(build_hash: permitted_params[:devbuild_hash])

          if permitted_params[:platform]
            scope = scope.where(platform: permitted_params[:platform])
          end

          status 200
          { result: scope.to_a }
        end

        desc 'Searches for a devbuild based on it being the build of the day or latest'
        params do
          requires :type, type: String, desc: 'Type to find by'
          requires :platform, type: String, desc: 'Platform to search a build for'
        end
        post 'find' do
          link, user = active_code

          build = nil

          if permitted_params[:type] == 'botd'
            # TODO: is an index needed for this lookup?
            build = DevBuild.where(platform: permitted_params[:platform],
                                   build_of_the_day: true).take(1).first
          elsif permitted_params[:type] == 'latest'
            build = DevBuild.order('id DESC').where(
              platform: permitted_params[:platform]
            ).where('verified = TRUE OR anonymous = FALSE').take(1).first
          else
            error!({ error_code: 400, message: 'Invalid type' }, 400)
          end

          unless build
            error!({
                     error_code: 404,
                     message: "Could not find build with type #{permitted_params[:type]}"
                   }, 404)
          end

          status 200
          build
        end

        desc 'Downloads specified list of dehydrated object'
        params do
          optional :objects, type: Array, desc: 'Objects to download. sha3 key needed'
        end
        post 'dehydrated/download' do
          if permitted_params[:objects].size > MAX_DEHYDRATED_DOWNLOAD_BATCH
            error!(
              {
                error_code: 400,
                message:
                  "Too many objects, max batch size is: #{MAX_DEHYDRATED_DOWNLOAD_BATCH}"
              }, 400
            )
          end

          link, user = active_code

          status 200
          {
            downloads: permitted_params[:objects].map { |obj|
                         get_download_with_sha3 user, obj['sha3']
                       }
          }
        end
      end
    end
  end
end
