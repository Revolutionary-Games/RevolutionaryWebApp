# frozen_string_literal: true

module API
  module V1
    # Patreon webhook receiving
    class PatreonWebhook < Grape::API
      include API::V1::Defaults

      resource :webhook do
        desc 'Patreon webhook endpoint'
        params do
          requires :webhook_id, type: String, desc: 'webhook ID'
        end
        post 'patreon' do
          settings = PatreonSettings.find_by webhook_id: permitted_params[:webhook_id]

          unless settings
            error!({ error_code: 403, message: 'Invalid webhook key or invalid signature' },
                   403)
          end

          status 200
          { 'stuff' => true }
        end
      end
    end
  end
end
