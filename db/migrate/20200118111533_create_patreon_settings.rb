# frozen_string_literal: true

class CreatePatreonSettings < ActiveRecord::Migration[5.2]
  def change
    create_table :patreon_settings do |t|
      t.boolean :active
      t.string :creator_token
      t.string :creator_refresh_token
      t.string :campaign_id
      t.string :webhook_secret
      t.integer :devbuilds_pledge_cents
      t.integer :vip_pledge_cents
      t.timestamp :last_refreshed
      t.timestamp :last_webhook

      t.timestamps
    end
  end
end
