# frozen_string_literal: true

module AdminOps
  # Creates a patreon settings object
  class CreatePatreonSettings < Hyperstack::ServerOp
    param :acting_user
    param :active
    param :creator_token
    param :creator_refresh_token
    param :campaign_id, nils: true
    param :webhook_secret, nils: true
    param :devbuilds_reward_id, nils: true
    param :vip_reward_id, nils: true
    validate { params.acting_user.admin? }
    step {
      params.devbuilds_reward_id = nil if params.devbuilds_reward_id.blank?

      params.vip_reward_id = nil if params.vip_reward_id.blank?

      PatreonSettings.create!(active: params.active,
                              creator_token: params.creator_token,
                              creator_refresh_token: params.creator_refresh_token,
                              campaign_id: params.campaign_id,
                              webhook_secret: params.webhook_secret,
                              devbuilds_reward_id: params.devbuilds_reward_id,
                              vip_reward_id: params.vip_reward_id)
    }
  end
end
