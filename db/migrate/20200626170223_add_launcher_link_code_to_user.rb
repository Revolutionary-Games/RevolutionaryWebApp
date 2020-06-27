class AddLauncherLinkCodeToUser < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :launcher_link_code, :string
    add_column :users, :launcher_code_expires, :timestamp

    add_index :users, :launcher_link_code, unique: true
  end
end
