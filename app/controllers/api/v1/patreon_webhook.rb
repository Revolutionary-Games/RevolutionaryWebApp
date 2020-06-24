# frozen_string_literal: true

module API
  module V1
    # Patreon webhook receiving
    class PatreonWebhook < Grape::API
      include API::V1::Defaults

      # We need the raw data for signature calculation
      parser :json, nil

      helpers do
        def hook_type
          event_type = headers['X-Patreon-Event']
          case event_type
          when 'pledges:create'
            :create
          when 'pledges:update'
            :update
          when 'pledges:delete'
            :delete
          else
            logger.warn "Unknown event type in webhook: #{event_type}"
            error!({ error_code: 400, message: 'Unknown event type' },
                   400)
          end
        end

        def check_headers_exist
          if headers.include?('X-Patreon-Signature') && headers.include?('X-Patreon-Event')
            return
          end

          error!({ error_code: 400, message: 'Missing patreon headers' },
                 400)
        end

        def verify_signature(payload, settings)
          if settings.webhook_secret.blank?
            error!({ error_code: 500, message: 'webhook secret is not configured' },
                   500)
          end

          needed_signature = OpenSSL::HMAC.hexdigest('MD5', settings.webhook_secret, payload)
          actual_signature = headers['X-Patreon-Signature']

          logger.debug "webhook signatures (given) #{actual_signature} | " \
                       "#{needed_signature} (needed)"

          if needed_signature == actual_signature
            # good signature
            return
          end

          error!({ error_code: 403, message: 'Invalid webhook key or invalid signature' },
                 403)
        end
      end

      resource :webhook do
        desc 'Patreon webhook endpoint'
        params do
          requires :webhook_id, type: String, desc: 'webhook ID'
        end
        post 'patreon' do
          check_headers_exist
          event_type = hook_type

          settings = PatreonSettings.find_by webhook_id: permitted_params[:webhook_id]

          if !settings || settings.active != true
            error!({ error_code: 403, message: 'Invalid webhook key or invalid signature' },
                   403)
          end

          body = env['api.request.body']

          if body.blank?
            error!({ error_code: 400, message: 'Request body is empty' },
                   400)
          end

          # logger.debug "body is: '#{body}'"

          verify_signature body, settings

          begin
            data = JSON.parse(body)
          rescue StandardError
            error!({ error_code: 400, message: 'Invalid JSON' }, 400)
          end

          pledge = data['data']

          patron_id = pledge['relationships']['patron']['data']['id']

          user_data = PatreonAPI.find_included_object data, patron_id

          unless user_data
            error!({ error_code: 400,
                     message: "Included objects didn't contain relevant user object" }, 400)
          end

          email = user_data['attributes']['email']

          if event_type == :delete
            # We need to just look up the email and delete the corresponding Patron object
            patron = Patron.find_by email: email

            if patron
              CheckSsoUserSuspensionJob.perform_later patron.email
              patron.destroy
            else
              logger.warn 'Could not find patron to delete'
            end
          elsif %i[create update].include? event_type

            PatreonGroupHelper.handle_patreon_pledge_obj pledge, user_data
          else
            error!({ error_code: 500, message: 'missing handler for event type' },
                   500)
          end

          # Queue a job to update the status for the relevant user
          ApplySinglePatronGroups.perform_later email

          settings.last_webhook = Time.now
          settings.save

          status 200
          { 'success' => true }
        end
      end
    end
  end
end
