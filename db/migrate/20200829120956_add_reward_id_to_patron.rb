class AddRewardIdToPatron < ActiveRecord::Migration[5.2]
  def change
    add_column :patrons, :reward_id, :string
  end
end
