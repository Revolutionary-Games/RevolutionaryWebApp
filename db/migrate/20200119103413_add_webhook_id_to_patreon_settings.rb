class AddWebhookIdToPatreonSettings < ActiveRecord::Migration[5.2]
  def change
    add_column :patreon_settings, :webhook_id, :string
    add_index :patreon_settings, :webhook_id, unique: true
  end
end
