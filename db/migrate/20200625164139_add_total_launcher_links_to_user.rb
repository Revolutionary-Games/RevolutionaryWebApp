class AddTotalLauncherLinksToUser < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :total_launcher_links, :integer, default: 0
  end
end
