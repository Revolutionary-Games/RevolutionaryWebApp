# frozen_string_literal: true

# Settings for the patreon group integration
class PatreonSettings < ApplicationRecord
  default_scope server: -> { all },
                client: -> { true }

  validates :active, inclusion: { in: [true, false] }
  validates :creator_token, presence: true, uniqueness: true
  validates :creator_refresh_token, presence: true

  validates :webhook_id, presence: true, uniqueness: true
  validate :check_reward_ids
  before_validation :generate_webhook_id

  def find_campaign_id_if_missing
    return unless campaign_id.blank?

    self.campaign_id = PatreonAPI.query_first_campaign_id creator_token

    raise 'Failed to find campaign id' if campaign_id.blank?
  end

  def find_reward_ids(devbuilds_name = 'Devbuilds Supporter', vip_name = 'VIP Supporter')
    self.devbuilds_reward_id = PatreonAPI.query_reward_by_title creator_token, campaign_id,
                                                                devbuilds_name

    raise 'Failed to find devbuilds reward id' if devbuilds_reward_id.blank?

    self.vip_reward_id = PatreonAPI.query_reward_by_title creator_token, campaign_id,
                                                          vip_name

    raise 'Failed to find vip reward id' if vip_reward_id.blank?
  end

  def all_patrons
    raise 'Invalid state for patreon settings' if campaign_id.blank? || creator_token.blank?

    PatreonAPI.query_all_current_patrons creator_token, campaign_id
  end

  def generate_webhook_id
    return if webhook_id

    new_id = nil
    loop do
      new_id = SecureRandom.base58(8)
      break unless PatreonSettings.find_by_webhook_id(new_id)
    end
    self.webhook_id = new_id
  end

  def check_reward_ids
    unless devbuilds_reward_id.nil?
      if devbuilds_reward_id.blank?
        errors.add(:devbuilds_reward_id, "can't be blank")
      else
        if devbuilds_reward_id == vip_reward_id
          errors.add(:devbuilds_reward_id, "can't be the same as vip reward")
        end
      end
    end

    unless vip_reward_id.nil?
      errors.add(:vip_reward_id, "can't be blank") if vip_reward_id.blank?
    end
  end

  server_method :webhook_url, default: '' do
    url = URI.join(ENV['BASE_URL'],
                   '/api/v1/webhook/patreon').to_s

    url + "?webhook_id=#{webhook_id}"
  end
end
