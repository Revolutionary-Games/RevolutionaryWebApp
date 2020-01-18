# frozen_string_literal: true

# Creates a patreon settings object
class CreatePatreonSettings < Hyperstack::ServerOp
  param :acting_user
  param :active
  param :creator_token
  param :creator_refresh_token
  param :campaign_id, nils: true
  param :webhook_secret, nils: true
  param :devbuilds_pledge_cents
  param :vip_pledge_cents
  validate { params.acting_user.admin? }
  step {
    PatreonSettings.create!(active: params.active,
                            creator_token: params.creator_token,
                            creator_refresh_token: params.creator_refresh_token,
                            campaign_id: params.campaign_id,
                            webhook_secret: params.webhook_secret,
                            devbuilds_pledge_cents: params.devbuilds_pledge_cents,
                            vip_pledge_cents: params.vip_pledge_cents)
  }
end
