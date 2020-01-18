# frozen_string_literal: true

# Settings for the patreon group integration
class PatreonSettings < ApplicationRecord
  default_scope server: -> { all },
                client: -> { true }

  validates :active, presence: true, inclusion: { in: [true, false] }
  validates :creator_token, presence: true, uniqueness: true
  validates :creator_refresh_token, presence: true

  validates_numericality_of :devbuilds_pledge_cents, less_than: :vip_pledge_cents
  validates_numericality_of :devbuilds_pledge_cents, greater_than: 1

  validates_numericality_of :vip_pledge_cents, less_than: 10_000
  validates_numericality_of :vip_pledge_cents, greater_than: :devbuilds_pledge_cents

  validates :creator_refresh_token, presence: true

  def find_campaign_id_if_missing
    return unless campaign_id.blank?

    self.campaign_id = PatreonAPI.query_first_campaign_id creator_token

    raise 'Failed to find campaign id' if campaign_id.blank?
  end

  def all_patrons
    raise 'Invalid state for patreon settings' if campaign_id.blank? || creator_token.blank?

    PatreonAPI.query_all_current_patrons creator_token, campaign_id
  end
end
