class CreateLauncherLinks < ActiveRecord::Migration[5.2]
  def change
    create_table :launcher_links do |t|
      t.string :link_code
      t.string :last_ip
      t.timestamp :last_connection
      t.integer :total_api_calls
      t.references :user, foreign_key: true

      t.timestamps

      t.index :link_code, unique: true
    end
  end
end
