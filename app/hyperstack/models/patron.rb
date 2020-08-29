# frozen_string_literal: true

# Our side of data of a Patron
class Patron < ApplicationRecord
  default_scope server: -> { all },
                client: -> { true }

  scope :sort_by_created_at,
        server: -> { order('created_at DESC') },
        select: -> { sort { |a, b| b.created_at <=> a.created_at } }

  scope :sort_by_updated_at,
        server: -> { order('updated_at DESC') },
        select: -> { sort { |a, b| b.updated_at <=> a.updated_at } }

  scope :paginated, lambda { |off, count|
    offset(off).take(count)
  }

  validates :suspended, inclusion: { in: [true, false] }, default: false
  validates :email, presence: true, uniqueness: true, length: { maximum: 255 }
  validates :email_alias, uniqueness: true, length: { maximum: 255 }, allow_nil: true
  validates :username, length: { maximum: 255 }, allow_nil: true

  validates_numericality_of :pledge_amount_cents, greater_than_equal: 0

  server_method :has_patreon_token?, default: '-' do
    (!patreon_token.blank?).to_s
  end

  def pledge
    # TODO store the currency as well as it might not always be dollars
    "#{pledge_amount_cents / 100.0}$"
  end

  def alias_or_email
    if email_alias.blank?
      email
    else
      email_alias
    end
  end

  def devbuilds?(patreon_settings)
    reward_id == patreon_settings.devbuilds_reward_id ||
      reward_id == patreon_settings.vip_reward_id
  end
end
