# frozen_string_literal: true

class AddNewRewardNamesToPatreonSettings < ActiveRecord::Migration[5.2]
  def change
    remove_column :patreon_settings, :devbuilds_pledge_cents, :integer
    remove_column :patreon_settings, :vip_pledge_cents, :integer
    add_column :patreon_settings, :devbuilds_reward_id, :string
    add_column :patreon_settings, :vip_reward_id, :string
  end
end
