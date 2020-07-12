# frozen_string_literal: true

module API
  module V1
    # Single LFS file getting endpoint that allows browsers
    class Launcher < Grape::API
      include API::V1::Defaults

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

          Rails.logger.info "Launcher link of user (#{user.email}) used from #{request.ip}"

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

          LauncherLink.create! user: user, link_code: link_code, last_ip: request.ip,
                               total_api_calls: 0

          { connected: true }
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

        desc 'Gets currently available devbuild information (latest builds + BOTD)'
        params do
        end
        get 'builds' do
          link, user = active_code

          { devbuilds: [] }
        end

        desc 'Searches for a devbuild based on the commit hash'
        params do
          requires :devbuild_hash, type: String, desc: 'Thing to search for'
        end
        get 'search' do
          link, user = active_code

          { result: [] }
        end

        desc 'Gets info for downloading a devbuild'
        params do
          requires :devbuild, type: Integer, desc: 'Devbuild id'
        end
        get 'builds/:devbuild' do
          link, user = active_code

          { stuff: 'devbuild info' }
        end
      end
    end
  end
end
