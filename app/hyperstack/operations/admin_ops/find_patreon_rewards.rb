# frozen_string_literal: true

module AdminOps
  # Finds the patreon rewards with name and saves them
  class FindPatreonRewards < Hyperstack::ServerOp
    param :acting_user
    param :patreon_settings_id
    param :devbuilds_name
    param :vip_name
    add_error(:devbuilds_name, :is_blank, 'reward name is blank') {
      params.devbuilds_name.blank?
    }
    add_error(:vip_name, :is_blank, 'reward name is blank') { params.vip_name.blank? }
    validate { params.acting_user.admin? }
    add_error(:patreon_settings_id, :does_not_exist, 'patreon settings do not exist') {
      !(@settings = PatreonSettings.find_by_id(params.patreon_settings_id))
    }
    step {
      @settings.find_reward_ids params.devbuilds_name, params.vip_name
    }
    step {
      @settings.save!
    }
    step {
      [@settings.devbuilds_reward_id, @settings.vip_reward_id]
    }
  end
end
