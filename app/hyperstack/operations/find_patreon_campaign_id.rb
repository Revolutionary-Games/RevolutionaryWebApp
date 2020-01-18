# frozen_string_literal: true

# Finds the first campaign for a patreon settings
class FindPatreonCampaignId < Hyperstack::ServerOp
  param :acting_user
  param :patreon_settings_id
  add_error(:patreon_settings_id, :does_not_exist, 'patreon settings do not exist') {
    !(@settings = PatreonSettings.find_by_id(params.patreon_settings_id))
  }
  validate { params.acting_user.admin? }
  step {
    @settings.campaign_id = nil
    @settings.find_campaign_id_if_missing
  }
  step {
    @settings.save!
  }
  step {
    @settings.campaign_id
  }
end
