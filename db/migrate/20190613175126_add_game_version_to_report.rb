class AddGameVersionToReport < ActiveRecord::Migration[5.2]
  def change
    add_column :reports, :game_version, :string
  end
end
