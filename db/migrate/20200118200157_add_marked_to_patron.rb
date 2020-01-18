class AddMarkedToPatron < ActiveRecord::Migration[5.2]
  def change
    add_column :patrons, :marked, :boolean
  end
end
