class AddSuspendedToUser < ActiveRecord::Migration[5.2]
  def change
    add_column :users, :suspended, :boolean, default: false
    add_column :users, :suspended_reason, :string
    add_column :users, :suspended_manually, :boolean, default: false
  end
end
